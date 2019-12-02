﻿using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using CSharpFunctionalExtensions;
using HappyTravel.Edo.Api.Infrastructure;
using HappyTravel.Edo.Api.Infrastructure.Constants;
using HappyTravel.Edo.Api.Infrastructure.Logging;
using HappyTravel.Edo.Api.Models.Payments.Payfort;
using HappyTravel.Edo.Common.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace HappyTravel.Edo.Api.Services.Payments
{
    public class PayfortService : IPayfortService
    {
        public PayfortService(ILogger<PayfortService> logger, IHttpClientFactory clientFactory, IOptions<PayfortOptions> options,
            IPayfortSignatureService signatureService)
        {
            _logger = logger;
            _clientFactory = clientFactory;
            _options = options.Value;
            _signatureService = signatureService;
        }


        public Task<Result<CreditCardPaymentResult>> Authorize(CreditCardPaymentRequest request) => MakePayment(request, PaymentType.Authorization);


        public Task<Result<CreditCardPaymentResult>> Pay(CreditCardPaymentRequest request) => MakePayment(request, PaymentType.Purchase);


        public Result<CreditCardPaymentResult> ProcessPaymentResponse(JObject response)
        {
            return GetModel<PayfortAuthorizeResponse>(response)
                .OnSuccess(CheckResponseSignature)
                .OnSuccess(CreateResult);

            Result<PayfortAuthorizeResponse> CheckResponseSignature(PayfortAuthorizeResponse model) => CheckSignature(response, model);


            CreditCardPaymentResult CreateResult(PayfortAuthorizeResponse model)
            {
                return new CreditCardPaymentResult(model, GetStatus(model));


                PaymentStatuses GetStatus(PayfortAuthorizeResponse payment)
                {
                    switch (payment.ResponseCode)
                    {
                        case PayfortConstants.PaymentSuccessResponseCode: return PaymentStatuses.Success;
                        case PayfortConstants.PaymentSecure3dResponseCode: return PaymentStatuses.Secure3d;
                        default: return PaymentStatuses.Failed;
                    }
                }
            }
        }


        public async Task<Result> Capture(CreditCardCaptureRequest request)
        {
            try
            {
                var requestContent = GetSignedContent();
                var client = _clientFactory.CreateClient(HttpClientNames.Payfort);
                using (var response = await client.PostAsync(_options.PaymentUrl, requestContent))
                {
                    return await GetContent(response)
                        .OnSuccess(GetJObject)
                        .OnSuccess(GetResponseModel)
                        .OnSuccess(CheckResponseSignature)
                        .OnSuccess(CreateResult);
                }
            }
            catch (Exception ex)
            {
                _logger.LogPayfortClientException(ex);
                return Result.Fail(ex.Message);
            }


            HttpContent GetSignedContent()
            {
                var paymentRequest = new PayfortCaptureRequest(
                    signature: string.Empty,
                    accessCode: _options.AccessCode,
                    merchantIdentifier: _options.Identifier,
                    merchantReference: request.ReferenceCode,
                    amount: ToPayfortAmount(request.Amount, request.Currency),
                    currency: request.Currency.ToString(),
                    language: request.LanguageCode,
                    fortId: request.ExternalId
                );

                var jObject = JObject.FromObject(paymentRequest, Serializer);
                var (_, _, signature, _) = _signatureService.Calculate(jObject, SignatureTypes.Request);
                paymentRequest = new PayfortCaptureRequest(paymentRequest, signature);
                var json = JsonConvert.SerializeObject(paymentRequest, SerializerSettings);

                return new StringContent(json, Encoding.UTF8, "application/json");
            }


            Result<(PayfortCaptureResponse model, JObject response)> GetResponseModel(JObject response)
                => GetModel<PayfortCaptureResponse>(response)
                    .Map(model => (model, response));


            Result<PayfortCaptureResponse> CheckResponseSignature((PayfortCaptureResponse model, JObject response) data)
                => CheckSignature(data.response, data.model);


            Result CreateResult(PayfortCaptureResponse model)
            {
                return IsSuccess(model)
                    ? Result.Ok()
                    : Result.Fail($"Unable capture payment for booking {request.ReferenceCode}: {model.ResponseMessage}");

                bool IsSuccess(PayfortCaptureResponse captureResponse) => captureResponse.ResponseCode == PayfortConstants.CaptureSuccessResponseCode;
            }
        }


        public async Task<Result> Void(CreditCardVoidRequest request)
        {
            try
            {
                var requestContent = GetSignedContent();
                var client = _clientFactory.CreateClient(HttpClientNames.Payfort);
                using (var response = await client.PostAsync(_options.PaymentUrl, requestContent))
                {
                    return await GetContent(response)
                        .OnSuccess(GetJObject)
                        .OnSuccess(GetResponseModel)
                        .OnSuccess(CheckResponseSignature)
                        .OnSuccess(CreateResult);
                }
            }
            catch (Exception ex)
            {
                _logger.LogPayfortClientException(ex);
                return Result.Fail(ex.Message);
            }


            HttpContent GetSignedContent()
            {
                var paymentRequest = new PayfortVoidRequest(
                    signature: string.Empty,
                    accessCode: _options.AccessCode,
                    merchantIdentifier: _options.Identifier,
                    merchantReference: request.ReferenceCode,
                    amount: ToPayfortAmount(request.Amount, request.Currency),
                    currency: request.Currency.ToString(),
                    language: request.LanguageCode,
                    fortId: request.ExternalId
                );

                var jObject = JObject.FromObject(paymentRequest, Serializer);
                var (_, _, signature, _) = _signatureService.Calculate(jObject, SignatureTypes.Request);
                paymentRequest = new PayfortVoidRequest(paymentRequest, signature);
                var json = JsonConvert.SerializeObject(paymentRequest, SerializerSettings);

                return new StringContent(json, Encoding.UTF8, "application/json");
            }


            Result<(PayfortVoidResponse model, JObject response)> GetResponseModel(JObject response)
                => GetModel<PayfortVoidResponse>(response)
                    .Map(model => (model, response));


            Result<PayfortVoidResponse> CheckResponseSignature((PayfortVoidResponse model, JObject response) data) => CheckSignature(data.response, data.model);


            Result CreateResult(PayfortVoidResponse model)
            {
                return IsSuccess(model)
                    ? Result.Ok()
                    : Result.Fail($"Unable capture payment for booking {request.ReferenceCode}: {model.ResponseMessage}");

                bool IsSuccess(PayfortVoidResponse captureResponse) => captureResponse.ResponseCode == PayfortConstants.VoidSuccessResponseCode;
            }
        }


        async Task<Result<CreditCardPaymentResult>> MakePayment(CreditCardPaymentRequest request, PaymentType type)
        {
            try
            {
                var requestContent = GetSignedContent();
                var client = _clientFactory.CreateClient(HttpClientNames.Payfort);
                using (var response = await client.PostAsync(_options.PaymentUrl, requestContent))
                {
                    return await GetContent(response)
                        .OnSuccess(GetJObject)
                        .OnSuccess(ProcessPaymentResponse);
                }
            }
            catch (Exception ex)
            {
                _logger.LogPayfortClientException(ex);
                return Result.Fail<CreditCardPaymentResult>(ex.Message);
            }


            HttpContent GetSignedContent()
            {
                var paymentRequest = new PayfortAuthorizeRequest(
                    signature: string.Empty,
                    accessCode: _options.AccessCode,
                    merchantIdentifier: _options.Identifier,
                    merchantReference: request.ReferenceCode,
                    amount: ToPayfortAmount(request.Amount, request.Currency),
                    currency: request.Currency.ToString(),
                    customerName: request.CustomerName,
                    customerEmail: request.CustomerEmail,
                    customerIp: request.CustomerIp,
                    language: request.LanguageCode,
                    returnUrl: _options.ReturnUrl,
                    settlementReference: request.ReferenceCode,
                    tokenName: request.Token.Code,
                    rememberMe: ToPayfortBoolean(request.Token.Type == PaymentTokenTypes.Stored),
                    cardSecurityCode: GetSecurityCode(),
                    command: GetCommand()
                );

                var jObject = JObject.FromObject(paymentRequest, Serializer);
                var (_, _, signature, _) = _signatureService.Calculate(jObject, SignatureTypes.Request);
                paymentRequest = new PayfortAuthorizeRequest(paymentRequest, signature);
                var json = JsonConvert.SerializeObject(paymentRequest, SerializerSettings);
                return new StringContent(json, Encoding.UTF8, "application/json");


                // Is not needed for new card.
                string GetSecurityCode() => request.IsNewCard ? null : request.SecurityCode;


                string GetCommand() => type == PaymentType.Purchase ? "PURCHASE" : "AUTHORIZATION";
            }
        }


        private Result<JObject> GetJObject(string content)
        {
            try
            {
                return Result.Ok(JObject.Parse(content));
            }
            catch (Exception ex)
            {
                _logger.LogPayfortError($"Error deserializing payfort response: '{content}'");
                return Result.Fail<JObject>($"{ex.Message} for '{content}'");

                ;
            }
        }


        private async Task<Result<string>> GetContent(HttpResponseMessage response)
        {
            var content = await response.Content.ReadAsStringAsync();
            return response.IsSuccessStatusCode
                ? Result.Ok(content)
                : Result.Fail<string>(content);
        }


        private Result<T> GetModel<T>(JObject response)
        {
            var model = response.ToObject<T>(Serializer);
            return model == null ? Result.Fail<T>($"Invalid payfort payment response: '{response}'") : Result.Ok(model);
        }


        private Result<T> CheckSignature<T>(JObject response, T model) where T : IPayfortResponse
        {
            var (_, _, signature, _) = _signatureService.Calculate(response, SignatureTypes.Response);
            if (signature != model.Signature)
            {
                _logger.LogPayfortError($"Payfort Payment error: Invalid response signature. Content: '{response}'");
                return Result.Fail<T>("Payfort process payment error");
            }

            return Result.Ok(model);
        }


        private static string ToPayfortBoolean(bool value) => value ? "YES" : "NO";


        private static string ToPayfortAmount(decimal amount, Currencies currency)
            => decimal.ToInt64(amount * PayfortConstants.ExponentMultipliers[currency]).ToString();


        private static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new SnakeCaseNamingStrategy()
            },
            NullValueHandling = NullValueHandling.Ignore
        };

        private static readonly JsonSerializer Serializer = JsonSerializer.Create(SerializerSettings);
        private readonly IHttpClientFactory _clientFactory;

        private readonly ILogger<PayfortService> _logger;
        private readonly PayfortOptions _options;
        private readonly IPayfortSignatureService _signatureService;
    }
}