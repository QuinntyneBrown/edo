using System;
using System.Linq;
using System.Threading.Tasks;
using CSharpFunctionalExtensions;
using FluentValidation;
using HappyTravel.Edo.Api.Infrastructure;
using HappyTravel.Edo.Api.Infrastructure.FunctionalExtensions;
using HappyTravel.Edo.Api.Models.Agents;
using HappyTravel.Edo.Api.Models.Payments;
using HappyTravel.Edo.Api.Models.Payments.CreditCards;
using HappyTravel.Edo.Api.Models.Payments.Payfort;
using HappyTravel.Edo.Api.Services.Agents;
using HappyTravel.Edo.Api.Services.Payments.Payfort;
using HappyTravel.Edo.Common.Enums;
using HappyTravel.Edo.Data;
using HappyTravel.Edo.Data.Payments;
using HappyTravel.EdoContracts.General.Enums;
using HappyTravel.Money.Enums;
using HappyTravel.Money.Models;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HappyTravel.Edo.Api.Services.Payments.CreditCards
{
    public class CreditCardPaymentProcessingService : ICreditCardPaymentProcessingService
    {
        public CreditCardPaymentProcessingService(IPayfortResponseParser responseParser,
            EdoContext context,
            IAgentContext agentContext,
            ICreditCardsManagementService creditCardsManagementService,
            IEntityLocker locker,
            IDateTimeProvider dateTimeProvider,
            ICreditCardMoneyAuthorizationService moneyAuthorizationService,
            ICreditCardMoneyCaptureService captureService)
        {
            _responseParser = responseParser;
            _context = context;
            _agentContext = agentContext;
            _creditCardsManagementService = creditCardsManagementService;
            _locker = locker;
            _dateTimeProvider = dateTimeProvider;
            _moneyAuthorizationService = moneyAuthorizationService;
            _captureService = captureService;
        }
        
        
        public async Task<Result<PaymentResponse>> Authorize(NewCreditCardPaymentRequest request, 
            string languageCode, string ipAddress, IPaymentsService paymentsService)
        {
            var agent = await _agentContext.GetAgent();
            var (_, isFailure, servicePrice, error) = await paymentsService.GetServicePrice(request.ReferenceCode);
            if (isFailure)
                return Result.Fail<PaymentResponse>(error);
            
            return await Validate(request)
                .OnSuccess(Authorize)
                .OnSuccessIf(IsSaveCardNeeded, SaveCard)
                .OnSuccess(StorePaymentResults);


            static Result Validate(NewCreditCardPaymentRequest payment)
            {
                return GenericValidator<NewCreditCardPaymentRequest>
                    .Validate(p =>
                    {
                        if (payment.IsSaveCardNeeded)
                        {
                            p.RuleFor(r => r.CardInfo)
                                .ChildRules(c =>
                                {
                                    c.RuleFor(info => info.HolderName)
                                        .NotEmpty()
                                        .WithMessage($"{nameof(CreditCardInfo.HolderName)} is required to save card");
                                });
                        }
                    }, payment);
            }

            async Task<Result<CreditCardPaymentResult>> Authorize()
            {
                var tokenType = request.IsSaveCardNeeded
                    ? PaymentTokenTypes.Stored
                    : PaymentTokenTypes.OneTime;

                var cardPaymentRequest = await CreatePaymentRequest(servicePrice,
                    new PaymentTokenInfo(request.Token, tokenType),
                    ipAddress, request.ReferenceCode,
                    languageCode, agent);

                return await _moneyAuthorizationService.AuthorizeMoneyForService(cardPaymentRequest, agent);
            }
            
            bool IsSaveCardNeeded(CreditCardPaymentResult response)
                => request.IsSaveCardNeeded && response.Status != CreditCardPaymentStatuses.Failed;
            
            Task SaveCard(CreditCardPaymentResult _) => _creditCardsManagementService.Save(request.CardInfo, request.Token, agent);
            
            async Task<Result<PaymentResponse>> StorePaymentResults(CreditCardPaymentResult paymentResult)
            {
                var card = request.IsSaveCardNeeded
                    ? await _context.CreditCards.SingleOrDefaultAsync(c => c.Token == request.Token)
                    : null;
                
                var payment = await CreatePayment(ipAddress, servicePrice, card?.Id, paymentResult);
                var (_, isFailure, error) = await paymentsService.ProcessPaymentChanges(payment);

                return isFailure
                    ? Result.Fail<PaymentResponse>(error)
                    : Result.Ok(paymentResult.ToPaymentResponse());
            }
        }
        
        
        public async Task<Result<PaymentResponse>> Authorize(SavedCreditCardPaymentRequest request, string languageCode, 
            string ipAddress, IPaymentsService paymentsService)
        {
            var agent = await _agentContext.GetAgent();
            var (_, isFailure, servicePrice, error) = await paymentsService.GetServicePrice(request.ReferenceCode);
            if (isFailure)
                return Result.Fail<PaymentResponse>(error);

            return await Validate(request)
                .OnSuccess(Authorize)
                .OnSuccess(StorePaymentResults);
            
            static Result Validate(SavedCreditCardPaymentRequest payment)
            {
                return GenericValidator<SavedCreditCardPaymentRequest>
                    .Validate(p =>
                    {
                        p.RuleFor(p => p.CardId).NotEmpty();
                    }, payment);
            }

            async Task<Result<CreditCardPaymentResult>> Authorize()
            {
                var (_, isFailure, token, error) = await _creditCardsManagementService.GetToken(request.CardId, agent);
                if(isFailure)
                    return Result.Fail<CreditCardPaymentResult>(error);
                
                var cardPaymentRequest = await CreatePaymentRequest(servicePrice,
                    new PaymentTokenInfo(token, PaymentTokenTypes.Stored),
                    ipAddress,
                    request.ReferenceCode,
                    languageCode,
                    agent,
                    request.SecurityCode);
                
                return await _moneyAuthorizationService.AuthorizeMoneyForService(cardPaymentRequest, agent);
            }
            
            async Task<Result<PaymentResponse>> StorePaymentResults(CreditCardPaymentResult paymentResult)
            {
                var payment = await CreatePayment(ipAddress, servicePrice, request.CardId, paymentResult);
                var (_, isFailure, error) = await paymentsService.ProcessPaymentChanges(payment);

                return isFailure
                    ? Result.Fail<PaymentResponse>(error)
                    : Result.Ok(paymentResult.ToPaymentResponse());
            }
        }
        
        private async Task<Payment> CreatePayment(string ipAddress, MoneyAmount moneyAmount,
            int? cardId, CreditCardPaymentResult paymentResult)
        {
            var now = _dateTimeProvider.UtcNow();
            var info = new CreditCardPaymentInfo(ipAddress, 
                paymentResult.ExternalCode,
                paymentResult.Message, 
                paymentResult.AuthorizationCode, 
                paymentResult.ExpirationDate,
                paymentResult.MerchantReference);

            var payment = new Payment
            {
                Amount = paymentResult.Amount,
                AccountNumber = paymentResult.CardNumber,
                Currency = moneyAmount.Currency.ToString(),
                Created = now,
                Modified = now,
                Status = paymentResult.Status.ToPaymentStatus(),
                Data = JsonConvert.SerializeObject(info),
                AccountId = cardId,
                PaymentMethod = PaymentMethods.CreditCard,
                ReferenceCode = paymentResult.ReferenceCode
            };
                
            _context.Payments.Add(payment);
            await _context.SaveChangesAsync();
            return payment;
        }


        private async Task<CreditCardPaymentRequest> CreatePaymentRequest(MoneyAmount moneyAmount,
            PaymentTokenInfo paymentToken,
            string ipAddress,
            string referenceCode,
            string languageCode,
            AgentInfo agent,
            string securityCode = default)
        {
            return new CreditCardPaymentRequest(currency: moneyAmount.Currency,
                amount: moneyAmount.Amount,
                token: paymentToken,
                customerName: $"{agent.FirstName} {agent.LastName}",
                customerEmail: agent.Email,
                customerIp: ipAddress,
                referenceCode: referenceCode,
                languageCode: languageCode,
                securityCode: securityCode,
                isNewCard: string.IsNullOrWhiteSpace(securityCode),
                merchantReference: await GetMerchantReference());


            async Task<string> GetMerchantReference()
            {
                var count = await _context.Payments.Where(p => p.ReferenceCode == referenceCode).CountAsync();
                return count == 0
                    ? referenceCode
                    : $"{referenceCode}-{count}";
            }
        }
        
        public async Task<Result<PaymentResponse>> ProcessPaymentResponse(JObject rawResponse, IPaymentsService paymentsService)
        {
            var (_, isParseFailure, paymentResponse, parseError) = _responseParser.ParsePaymentResponse(rawResponse);
            if (isParseFailure)
                return Result.Fail<PaymentResponse>(parseError);
            
            return await LockPayment()
                .OnSuccess(ProcessPaymentResponse)
                .OnSuccess(StorePaymentResults)
                .OnBoth(UnlockPayment);

            Task<Result> LockPayment() => _locker.Acquire<Payment>(paymentResponse.ReferenceCode, nameof(CreditCardPaymentProcessingService));
            
            async Task<Result<(CreditCardPaymentResult, Payment)>> ProcessPaymentResponse()
            {
                var agent = await _agentContext.GetAgent();
                var (_, isFailure, payment, error) = await GetPaymentForResponse(paymentResponse);
                if (isFailure)
                    return Result.Fail<(CreditCardPaymentResult, Payment)>(error);
                
                // Payment can be completed before. Nothing to do now.
                if (payment.Status == PaymentStatuses.Authorized)
                    return Result.Ok((GetDefaultSuccessResult(), payment));
                
                var (_, isProcessFailure, processResult, processError) = await _moneyAuthorizationService.ProcessPaymentResponse(paymentResponse,
                    Enum.Parse<Currencies>(payment.Currency),
                    agent);
                
                if(isProcessFailure)
                    return Result.Fail<(CreditCardPaymentResult, Payment)>(processError);
                
                return Result.Ok((processResult, payment));
                
                static CreditCardPaymentResult GetDefaultSuccessResult() => new CreditCardPaymentResult(string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    CreditCardPaymentStatuses.Success,
                    string.Empty,
                    default,
                    string.Empty);
            }
            
            
            async Task<PaymentResponse> StorePaymentResults((CreditCardPaymentResult, Payment) result)
            {
                var (paymentResult, payment) = result;
                // Payment can be completed before. Nothing to do now.
                if (payment.Status == PaymentStatuses.Authorized)
                    return paymentResult.ToPaymentResponse();
                
                var info = JsonConvert.DeserializeObject<CreditCardPaymentInfo>(payment.Data);
                var newInfo = new CreditCardPaymentInfo(info.CustomerIp, paymentResult.ExternalCode, paymentResult.Message, paymentResult.AuthorizationCode,
                    paymentResult.ExpirationDate, info.InternalReferenceCode);
                payment.Status = paymentResult.Status.ToPaymentStatus();
                payment.Data = JsonConvert.SerializeObject(newInfo);
                payment.Modified = _dateTimeProvider.UtcNow();
                _context.Update(payment);
                await _context.SaveChangesAsync();
                await paymentsService.ProcessPaymentChanges(payment);

                return paymentResult.ToPaymentResponse();
            }

            async Task<Result<Payment>> GetPaymentForResponse(CreditCardPaymentResult paymentResponse)
            {
                var payment = await _context.Payments
                    .SingleOrDefaultAsync(p => p.ReferenceCode == paymentResponse.ReferenceCode);

                if (payment == default)
                    return Result.Fail<Payment>($"Could not find a payment record with the reference code {paymentResponse.ReferenceCode}");

                if (payment.Amount != paymentResponse.Amount)
                    return Result.Fail<Payment>($"Invalid payment amount, expected: '{payment.Amount}', actual: '{paymentResponse.Amount}'");
                
                return Result.Ok(payment);
            }


            async Task<Result<PaymentResponse>> UnlockPayment(Result<PaymentResponse> result)
            {
                await _locker.Release<Payment>(paymentResponse.ReferenceCode);
                return result;
            }
        }
        
        public async Task<Result<string>> CaptureMoney(string referenceCode, IPaymentsService paymentsService)
        {
            var payment = await _context.Payments.SingleOrDefaultAsync(p => p.ReferenceCode == referenceCode);
            if (payment.PaymentMethod != PaymentMethods.CreditCard)
                return Result.Fail<string>($"Invalid payment method: {payment.PaymentMethod}");

            return await Capture()
                .OnSuccess(StoreCaptureResults)
                .OnBoth(CreateResult);

            async Task<Result<CreditCardCaptureResult>> Capture()
            {
                var (_, isFailure, servicePrice, error) = await paymentsService.GetServicePrice(referenceCode);
                if (isFailure)
                    return Result.Fail<CreditCardCaptureResult>(error);
                
                var paymentInfo = JsonConvert.DeserializeObject<CreditCardPaymentInfo>(payment.Data);

                var request = new CreditCardCaptureMoneyRequest(currency: servicePrice.Currency,
                    amount: servicePrice.Amount,
                    externalId: paymentInfo.ExternalId,
                    merchantReference: paymentInfo.InternalReferenceCode,
                    languageCode: "en");

                return await _captureService.Capture(request,
                    paymentInfo,
                    payment.AccountNumber,
                    Enum.Parse<Currencies>(payment.Currency),
                    new AgentInfo());
            }
            
            
            async Task StoreCaptureResults(CreditCardCaptureResult captureResult)
            {
                payment.Status = PaymentStatuses.Captured;
                _context.Payments.Update(payment);
                await _context.SaveChangesAsync();
                await paymentsService.ProcessPaymentChanges(payment);
            }

            Result<string> CreateResult(Result<CreditCardCaptureResult> result)
                => result.IsSuccess
                    ? Result.Ok($"Payment for the payment '{payment.ReferenceCode}' completed.")
                    : Result.Fail<string>($"Unable to complete payment for the payment '{payment.ReferenceCode}'. Reason: {result.Error}");
        }
        
        public async Task<Result> VoidMoney(string referenceCode, IPaymentsService paymentsService)
        {
            var payment = await _context.Payments.SingleOrDefaultAsync(p => p.ReferenceCode == referenceCode);
            if (payment.PaymentMethod != PaymentMethods.CreditCard)
                return Result.Fail<string>($"Invalid payment method: {payment.PaymentMethod}");

            return await Void()
                .OnSuccess(StoreVoidResults);

            async Task<Result<CreditCardVoidResult>> Void()
            {
                var agent = await _agentContext.GetAgent();
                var info = JsonConvert.DeserializeObject<CreditCardPaymentInfo>(payment.Data);
                var request = new CreditCardVoidMoneyRequest(
                    externalId: info.ExternalId,
                    merchantReference: info.InternalReferenceCode,
                    languageCode: "en");

                return await _captureService.Void(request,
                    info,
                    payment.AccountNumber,
                    new MoneyAmount(payment.Amount, Enum.Parse<Currencies>(payment.Currency)),
                    payment.ReferenceCode, agent);
            }
            
            async Task StoreVoidResults(CreditCardVoidResult voidResult)
            {
                payment.Status = PaymentStatuses.Voided;
                _context.Payments.Update(payment);
                await _context.SaveChangesAsync();
                await paymentsService.ProcessPaymentChanges(payment);
            }
        }
        

        private readonly IPayfortResponseParser _responseParser;
        private readonly EdoContext _context;
        private readonly IAgentContext _agentContext;
        private readonly ICreditCardsManagementService _creditCardsManagementService;
        private readonly IEntityLocker _locker;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly ICreditCardMoneyAuthorizationService _moneyAuthorizationService;
        private readonly ICreditCardMoneyCaptureService _captureService;
    }
}