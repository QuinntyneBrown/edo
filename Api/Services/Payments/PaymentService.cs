using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CSharpFunctionalExtensions;
using FluentValidation;
using HappyTravel.Edo.Api.Infrastructure;
using HappyTravel.Edo.Api.Infrastructure.FunctionalExtensions;
using HappyTravel.Edo.Api.Infrastructure.Users;
using HappyTravel.Edo.Api.Models.Bookings;
using HappyTravel.Edo.Api.Models.Payments;
using HappyTravel.Edo.Api.Models.Payments.Payfort;
using HappyTravel.Edo.Api.Services.Accommodations;
using HappyTravel.Edo.Api.Services.Customers;
using HappyTravel.Edo.Api.Services.Management;
using HappyTravel.Edo.Common.Enums;
using HappyTravel.Edo.Data;
using HappyTravel.Edo.Data.Booking;
using HappyTravel.Edo.Data.Infrastructure.DatabaseExtensions;
using HappyTravel.Edo.Data.Payments;
using HappyTravel.EdoContracts.Accommodations.Enums;
using HappyTravel.EdoContracts.General.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HappyTravel.Edo.Api.Services.Payments
{
    public class PaymentService : IPaymentService
    {
        public PaymentService(IAdministratorContext adminContext,
            IPaymentProcessingService paymentProcessingService,
            EdoContext context,
            IPayfortService payfortService,
            IDateTimeProvider dateTimeProvider,
            IServiceAccountContext serviceAccountContext,
            ICreditCardService creditCardService,
            ICustomerContext customerContext,
            IPaymentNotificationService notificationService,
            IAccountManagementService accountManagementService,
            ILogger<PaymentService> logger)
        {
            _adminContext = adminContext;
            _paymentProcessingService = paymentProcessingService;
            _context = context;
            _payfortService = payfortService;
            _dateTimeProvider = dateTimeProvider;
            _serviceAccountContext = serviceAccountContext;
            _creditCardService = creditCardService;
            _customerContext = customerContext;
            _accountManagementService = accountManagementService;
            _logger = logger;
            _notificationService = notificationService;
        }


        public IReadOnlyCollection<Currencies> GetCurrencies() => new ReadOnlyCollection<Currencies>(Currencies);

        public IReadOnlyCollection<PaymentMethods> GetAvailableCustomerPaymentMethods() => new ReadOnlyCollection<PaymentMethods>(AvailablePaymentMethods);


        public Task<Result<PaymentResponse>> Pay(PaymentRequest request, string languageCode, string ipAddress, CustomerInfo customerInfo)
        {
            return Validate(request, customerInfo)
                .OnSuccess(CreateRequest)
                .OnSuccess(Pay)
                .OnSuccessIf(IsPaymentComplete, SendBillToCustomer)
                .OnSuccessWithTransaction(_context, payment => Result.Ok(payment.Item2)
                    .OnSuccess(StorePayment)
                    .OnSuccess(ChangePaymentStatusForBookingToAuthorized)
                    .OnSuccess(MarkCreditCardAsUsed)
                    .OnSuccess(CreateResponse));


            async Task<CreditCardPaymentRequest> CreateRequest()
            {
                var isNewCard = true;
                if (request.Token.Type == PaymentTokenTypes.Stored)
                {
                    var token = request.Token.Code;
                    var card = await _context.CreditCards.FirstAsync(c => c.Token == token);
                    isNewCard = card.IsUsedForPayments != true;
                }

                return new CreditCardPaymentRequest(currency: request.Currency,
                    amount: request.Amount,
                    token: request.Token,
                    customerName: $"{customerInfo.FirstName} {customerInfo.LastName}",
                    customerEmail: customerInfo.Email,
                    customerIp: ipAddress,
                    referenceCode: request.ReferenceCode,
                    languageCode: languageCode,
                    securityCode: request.SecurityCode,
                    isNewCard: isNewCard);
            }


            async Task<Result<(CreditCardPaymentRequest, CreditCardPaymentResult)>> Pay(CreditCardPaymentRequest paymentRequest)
            {
                var (_, isFailure, payment, error) = await _payfortService.Authorize(paymentRequest);
                if (isFailure)
                    return Result.Fail<(CreditCardPaymentRequest, CreditCardPaymentResult)>(error);

                return payment.Status == PaymentStatuses.Failed
                    ? Result.Fail<(CreditCardPaymentRequest, CreditCardPaymentResult)>($"Payment error: {payment.Message}")
                    : Result.Ok((paymentRequest, payment));
            }


            bool IsPaymentComplete((CreditCardPaymentRequest, CreditCardPaymentResult) creditCardPaymentData)
            {
                var (_, result) = creditCardPaymentData;
                return result.Status == PaymentStatuses.Success;
            }


            Task SendBillToCustomer((CreditCardPaymentRequest, CreditCardPaymentResult) creditCardPaymentData)
            {
                var (paymentRequest, _) = creditCardPaymentData;
                return _notificationService.SendBillToCustomer(new PaymentBill(paymentRequest.CustomerEmail,
                    paymentRequest.Amount,
                    paymentRequest.Currency,
                    _dateTimeProvider.UtcNow(),
                    PaymentMethods.CreditCard,
                    paymentRequest.ReferenceCode,
                    paymentRequest.CustomerName));
            }


            async Task StorePayment(CreditCardPaymentResult payment)
            {
                // ReferenceCode should always contain valid booking reference code. We check it in CheckReferenceCode or StorePayment
                var booking = await _context.Bookings.FirstAsync(b => b.ReferenceCode == request.ReferenceCode);

                var token = request.Token.Code;
                var card = request.Token.Type == PaymentTokenTypes.Stored
                    ? await _context.CreditCards.FirstOrDefaultAsync(c => c.Token == token)
                    : null;
                var now = _dateTimeProvider.UtcNow();
                var info = new CreditCardPaymentInfo(ipAddress, payment.ExternalCode, payment.Message, payment.AuthorizationCode, payment.ExpirationDate);
                _context.ExternalPayments.Add(new ExternalPayment
                {
                    Amount = request.Amount,
                    BookingId = booking.Id,
                    AccountNumber = payment.CardNumber,
                    Currency = request.Currency.ToString(),
                    Created = now,
                    Modified = now,
                    Status = payment.Status,
                    Data = JsonConvert.SerializeObject(info),
                    CreditCardId = card?.Id
                });

                await _context.SaveChangesAsync();
            }
        }


        public Task<Result<PaymentResponse>> ProcessPaymentResponse(JObject response)
        {
            return _payfortService.ProcessPaymentResponse(response)
                .OnSuccess(ProcessPaymentResponse);


            async Task<Result<PaymentResponse>> ProcessPaymentResponse(CreditCardPaymentResult paymentResult)
            {
                var booking = await _context.Bookings.FirstOrDefaultAsync(b => b.ReferenceCode == paymentResult.ReferenceCode);
                if (booking == null)
                    return Result.Fail<PaymentResponse>($"Could not find a booking by the reference code {paymentResult.ReferenceCode}");

                var paymentEntity = await _context.ExternalPayments.FirstOrDefaultAsync(p => p.BookingId == booking.Id);
                if (paymentEntity == null)
                    return Result.Fail<PaymentResponse>($"Could not find a payment record with the booking ID {booking.Id}");

                // Payment can be completed before. Nothing to do now.
                if (paymentEntity.Status == PaymentStatuses.Success)
                    return Result.Ok(new PaymentResponse(string.Empty, PaymentStatuses.Success, PaymentStatuses.Success.ToString()));

                return await Result.Ok(paymentResult)
                    .OnSuccessWithTransaction(_context, payment => Result.Ok(payment)
                        .OnSuccess(StorePayment)
                        .OnSuccess(CheckPaymentStatusNotFailed)
                        .OnSuccessIf(IsPaymentComplete, SendBillToCustomer)
                        .OnSuccess(p => ChangePaymentStatusForBookingToAuthorized(p, booking))
                        .OnSuccess(MarkCreditCardAsUsed)
                        .OnSuccess(CreateResponse));


                Result<CreditCardPaymentResult> CheckPaymentStatusNotFailed(CreditCardPaymentResult payment)
                    => payment.Status == PaymentStatuses.Failed
                        ? Result.Fail<CreditCardPaymentResult>($"Payment error: {payment.Message}")
                        : Result.Ok(payment);


                Task StorePayment(CreditCardPaymentResult payment)
                {
                    var info = JsonConvert.DeserializeObject<CreditCardPaymentInfo>(paymentEntity.Data);
                    var newInfo = new CreditCardPaymentInfo(info.CustomerIp, payment.ExternalCode, payment.Message, payment.AuthorizationCode,
                        payment.ExpirationDate);
                    paymentEntity.Status = payment.Status;
                    paymentEntity.Data = JsonConvert.SerializeObject(newInfo);
                    paymentEntity.Modified = _dateTimeProvider.UtcNow();
                    _context.Update(paymentEntity);
                    return _context.SaveChangesAsync();
                }


                bool IsPaymentComplete(CreditCardPaymentResult cardPaymentResult) => cardPaymentResult.Status == PaymentStatuses.Success;


                async Task SendBillToCustomer()
                {
                    var customer = await _context.Customers.SingleOrDefaultAsync(c => c.Id == booking.CustomerId);
                    if (customer == default)
                    {
                        _logger.LogWarning("Send bill after credit card payment: could not find customer with id '{0}' for booking '{1}'", booking.CustomerId,
                            booking.ReferenceCode);
                        return;
                    }

                    Enum.TryParse<Currencies>(paymentEntity.Currency, out var currency);
                    await _notificationService.SendBillToCustomer(new PaymentBill(customer.Email,
                        paymentEntity.Amount,
                        currency,
                        _dateTimeProvider.UtcNow(),
                        PaymentMethods.CreditCard,
                        booking.ReferenceCode,
                        $"{customer.LastName} {customer.FirstName}"));
                }
            }
        }


        public async Task<bool> CanPayWithAccount(CustomerInfo customerInfo)
        {
            var companyId = customerInfo.CompanyId;
            return await _context.PaymentAccounts
                .Where(a => a.CompanyId == companyId)
                .AnyAsync(a => a.Balance + a.CreditLimit > 0);
        }


        public async Task<Result<List<int>>> GetBookingsForCapture(DateTime deadlineDate)
        {
            if (deadlineDate == default)
                return Result.Fail<List<int>>("Deadline date should be specified");

            var (_, isFailure, _, error) = await _serviceAccountContext.GetUserInfo();
            if (isFailure)
                return Result.Fail<List<int>>(error);

            var bookings = await _context.Bookings
                .Where(booking =>
                    BookingStatusesForPayment.Contains(booking.Status) && booking.PaymentStatus == BookingPaymentStatuses.Authorized &&
                    (booking.PaymentMethod == PaymentMethods.BankTransfer || booking.PaymentMethod == PaymentMethods.CreditCard))
                .ToListAsync();

            var date = deadlineDate.Date;
            var bookingIds = bookings
                .Where(booking =>
                {
                    var availabilityInfo = JsonConvert.DeserializeObject<BookingAvailabilityInfo>(booking.ServiceDetails);
                    return availabilityInfo.Agreement.DeadlineDate.Date < date;
                })
                .Select(booking => booking.Id)
                .ToList();

            return Result.Ok(bookingIds);
        }


        public async Task<Result<ProcessResult>> CaptureMoneyForBookings(List<int> bookingIds)
        {
            var (_, isUserFailure, user, userError) = await _serviceAccountContext.GetUserInfo();
            if (isUserFailure)
                return Result.Fail<ProcessResult>(userError);

            var bookings = await GetBookings();

            return await Validate()
                .OnSuccess(ProcessBookings);


            Task<List<Booking>> GetBookings()
            {
                var ids = bookingIds;
                return _context.Bookings.Where(booking => ids.Contains(booking.Id)).ToListAsync();
            }


            Result Validate()
            {
                return bookings.Count != bookingIds.Count
                    ? Result.Fail("Invalid booking ids. Could not find some of requested bookings.")
                    : Result.Combine(bookings.Select(ValidateBooking).ToArray());


                Result ValidateBooking(Booking booking)
                    => GenericValidator<Booking>.Validate(v =>
                    {
                        v.RuleFor(c => c.PaymentStatus)
                            .Must(status => booking.PaymentStatus == BookingPaymentStatuses.Authorized)
                            .WithMessage(
                                $"Invalid payment status for booking '{booking.ReferenceCode}': {booking.PaymentStatus}");
                        v.RuleFor(c => c.Status)
                            .Must(status => BookingStatusesForPayment.Contains(status))
                            .WithMessage($"Invalid booking status for booking '{booking.ReferenceCode}': {booking.Status}");
                        v.RuleFor(c => c.PaymentMethod)
                            .Must(method => method == PaymentMethods.BankTransfer ||
                                method == PaymentMethods.CreditCard)
                            .WithMessage($"Invalid payment method for booking '{booking.ReferenceCode}': {booking.PaymentMethod}");
                    }, booking);
            }


            Task<ProcessResult> ProcessBookings()
            {
                return Combine(bookings.Select(ProcessBooking));


                Task<Result<string>> ProcessBooking(Booking booking)
                {
                    var bookingAvailability = JsonConvert.DeserializeObject<BookingAvailabilityInfo>(booking.ServiceDetails);
                    if (!Enum.TryParse<Currencies>(bookingAvailability.Agreement.Price.CurrencyCode, out var currency))
                        return Task.FromResult(Result.Fail<string>(
                            $"Unsupported currency in agreement: {bookingAvailability.Agreement.Price.CurrencyCode}"));

                    return Result.Ok(booking)
                        .OnSuccessWithTransaction(_context, _ =>
                            CapturePayment()
                                .OnSuccess(ChangePaymentStatusToCaptured)
                        )
                        .OnBoth(CreateResult);


                    Task<Result> CapturePayment()
                    {
                        switch (booking.PaymentMethod)
                        {
                            case PaymentMethods.BankTransfer:
                                return GetAccount()
                                    .OnSuccess(CaptureAccountPayment);
                            case PaymentMethods.CreditCard:
                                return GetPayment(booking)
                                    .OnSuccess(CaptureCreditCardPayment);
                            default: return Task.FromResult(Result.Fail($"Invalid payment method: {booking.PaymentMethod}"));
                        }

                        Task<Result<PaymentAccount>> GetAccount() => _accountManagementService.Get(booking.CompanyId, currency);


                        Task<Result> CaptureAccountPayment(PaymentAccount account)
                        {
                            // Hack. Error for updating same entity several times in different SaveChanges
                            _context.Detach(account);

                            return _paymentProcessingService.CaptureMoney(account.Id, new AuthorizedMoneyData(
                                    currency: account.Currency,
                                    amount: bookingAvailability.Agreement.Price.NetTotal,
                                    referenceCode: booking.ReferenceCode,
                                    reason: $"Capture money for booking '{booking.ReferenceCode}' after check-in"),
                                user);
                        }


                        Task<Result> CaptureCreditCardPayment(ExternalPayment payment)
                        {
                            var info = JsonConvert.DeserializeObject<CreditCardPaymentInfo>(payment.Data);
                            return _payfortService.Capture(new CreditCardCaptureMoneyRequest(currency: currency,
                                amount: bookingAvailability.Agreement.Price.NetTotal,
                                externalId: info.ExternalId,
                                referenceCode: booking.ReferenceCode,
                                languageCode: "en"));
                        }
                    }


                    Task ChangePaymentStatusToCaptured() => ChangeBookingPaymentStatusToCaptured(booking);


                    Result<string> CreateResult(Result result)
                        => result.IsSuccess
                            ? Result.Ok($"Payment for booking '{booking.ReferenceCode}' completed.")
                            : Result.Fail<string>($"Unable to complete payment for booking '{booking.ReferenceCode}'. Reason: {result.Error}");
                }
            }
        }


        public Task<Result> ReplenishAccount(int accountId, PaymentData payment)
        {
            return Result.Ok()
                .Ensure(HasPermission, "Permission denied")
                .OnSuccess(AddMoney);

            Task<bool> HasPermission() => _adminContext.HasPermission(AdministratorPermissions.AccountReplenish);


            Task<Result> AddMoney()
            {
                return GetUserInfo()
                    .OnSuccess(AddMoneyWithUser);


                Task<Result<UserInfo>> GetUserInfo()
                    => _adminContext.GetUserInfo()
                        .OnFailureCompensate(_serviceAccountContext.GetUserInfo)
                        .OnFailureCompensate(_customerContext.GetUserInfo);


                Task<Result> AddMoneyWithUser(UserInfo user)
                    => _paymentProcessingService.AddMoney(accountId,
                        payment,
                        user);
            }
        }


        public Task<Result> AuthorizeMoneyFromAccount(Booking booking, CustomerInfo customerInfo)
        {
            var bookingAvailability = JsonConvert.DeserializeObject<BookingAvailabilityInfo>(booking.ServiceDetails);

            if (!Enum.TryParse<Currencies>(bookingAvailability.Agreement.Price.CurrencyCode, out var currency))
            {
                return Task.FromResult(Result.Fail(
                    $"Unsupported currency in agreement: {bookingAvailability.Agreement.Price.CurrencyCode}"));
            }

            return Result.Ok()
                .Ensure(CanAuthorize, $"Could not authorize money for booking '{booking.ReferenceCode}")
                .OnSuccess(GetAccountAndUser)
                .OnSuccess(AuthorizeMoney)
                .OnSuccess(SendBillToCustomer);


            bool CanAuthorize()
                => booking.PaymentMethod == PaymentMethods.BankTransfer &&
                    BookingStatusesForAuthorization.Contains(booking.Status);


            async Task<Result<(PaymentAccount account, UserInfo user)>> GetAccountAndUser()
            {
                var (_, isUserFailure, user, userError) = await _customerContext.GetUserInfo();
                if (isUserFailure)
                    return Result.Fail<(PaymentAccount, UserInfo)>(userError);

                var result = await _accountManagementService.Get(customerInfo.CompanyId, currency);
                return result.Map(account => (account, user));
            }


            Task<Result> AuthorizeMoney((PaymentAccount account, UserInfo userInfo) data)
                => _paymentProcessingService.AuthorizeMoney(data.account.Id, new AuthorizedMoneyData(
                        currency: data.account.Currency,
                        amount: bookingAvailability.Agreement.Price.NetTotal,
                        reason: $"Authorize money after booking '{booking.ReferenceCode}'",
                        referenceCode: booking.ReferenceCode),
                    data.userInfo);


            async Task SendBillToCustomer()
            {
                var customer = await _context.Customers.SingleOrDefaultAsync(c => c.Id == booking.CustomerId);
                if (customer == default)
                {
                    _logger.LogWarning("Send bill after payment from account: could not find customer with id '{0}' for booking '{1}'", booking.CustomerId,
                        booking.ReferenceCode);
                    return;
                }

                await _notificationService.SendBillToCustomer(new PaymentBill(customer.Email,
                    bookingAvailability.Agreement.Price.NetTotal,
                    currency,
                    _dateTimeProvider.UtcNow(),
                    EdoContracts.General.Enums.PaymentMethods.BankTransfer,
                    booking.ReferenceCode,
                    $"{customer.LastName} {customer.FirstName}"));
            }
        }


        public Task<Result> VoidMoney(Booking booking)
        {
            // TODO: Implement refund money if status is paid with deadline penalty
            if (booking.PaymentStatus != BookingPaymentStatuses.Authorized)
                return Task.FromResult(Result.Ok());

            var bookingAvailability = JsonConvert.DeserializeObject<BookingAvailabilityInfo>(booking.ServiceDetails);

            if (!Enum.TryParse<Currencies>(bookingAvailability.Agreement.Price.CurrencyCode, out var currency))
                return Task.FromResult(Result.Fail($"Unsupported currency in agreement: {bookingAvailability.Agreement.Price.CurrencyCode}"));

            switch (booking.PaymentMethod)
            {
                case PaymentMethods.BankTransfer:
                    return GetCustomer()
                        .OnSuccess(GetAccount)
                        .OnSuccess(VoidMoneyFromAccount);
                case PaymentMethods.CreditCard:
                    return GetPayment(booking)
                        .OnSuccess(VoidMoneyFromCreditCard);
                default: return Task.FromResult(Result.Fail($"Could not void money for booking with payment method '{booking.PaymentMethod}'"));
            }

            async Task<Result<CustomerInfo>> GetCustomer() => await _customerContext.GetCustomerInfo();

            Task<Result<PaymentAccount>> GetAccount(CustomerInfo customerInfo) => _accountManagementService.Get(customerInfo.CompanyId, currency);


            Task<Result> VoidMoneyFromAccount(PaymentAccount account)
            {
                return GetUser()
                    .OnSuccess(userInfo =>
                        _paymentProcessingService.VoidMoney(account.Id, new AuthorizedMoneyData(bookingAvailability.Agreement.Price.NetTotal,
                            currency, reason: $"Void money after booking cancellation '{booking.ReferenceCode}'",
                            referenceCode: booking.ReferenceCode), userInfo));

                Task<Result<UserInfo>> GetUser() => _customerContext.GetUserInfo();
            }


            Task<Result> VoidMoneyFromCreditCard(ExternalPayment payment)
            {
                var info = JsonConvert.DeserializeObject<CreditCardPaymentInfo>(payment.Data);
                return _payfortService.Void(new CreditCardVoidMoneyRequest(
                    externalId: info.ExternalId,
                    referenceCode: booking.ReferenceCode,
                    languageCode: "en"));
            }
        }


        public async Task<Result> CompleteOffline(int bookingId)
        {
            return await _adminContext.GetCurrent()
                .OnSuccess(GetBooking)
                .OnSuccess(CheckBookingCanBeCompleted)
                .OnSuccess(Complete)
                .OnSuccess(SendBillToCustomer);


            async Task<Result<Booking>> GetBooking()
            {
                var entity = await _context.Bookings.FirstOrDefaultAsync(b => b.Id == bookingId);
                return entity == null
                    ? Result.Fail<Booking>($"Could not find booking with id {bookingId}")
                    : Result.Ok(entity);
            }


            Result<Booking> CheckBookingCanBeCompleted(Booking booking)
                => booking.PaymentStatus == BookingPaymentStatuses.NotPaid
                    ? Result.Ok(booking)
                    : Result.Fail<Booking>($"Could not complete booking. Invalid payment status: {booking.PaymentStatus}");


            Task Complete(Booking booking)
            {
                booking.PaymentMethod = PaymentMethods.Offline;
                return ChangeBookingPaymentStatusToCaptured(booking);
            }


            async Task SendBillToCustomer(Booking booking)
            {
                var availabilityInfo = JsonConvert.DeserializeObject<BookingAvailabilityInfo>(booking.ServiceDetails);

                if (!Enum.TryParse<Currencies>(availabilityInfo.Agreement.Price.CurrencyCode, out var currency))
                {
                    _logger.LogWarning("Send bill after offline payment: Unsupported currency in agreement: {0} for booking '{1}'",
                        availabilityInfo.Agreement.Price.CurrencyCode, booking.ReferenceCode);
                    return;
                }

                var customer = await _context.Customers.SingleOrDefaultAsync(c => c.Id == booking.CustomerId);
                if (customer == default)
                {
                    _logger.LogWarning("Send bill after offline payment: could not find customer with id '{0}' for booking '{1}'", booking.CustomerId,
                        booking.ReferenceCode);
                    return;
                }

                await _notificationService.SendBillToCustomer(new PaymentBill(customer.Email,
                    availabilityInfo.Agreement.Price.NetTotal,
                    currency,
                    _dateTimeProvider.UtcNow(),
                    PaymentMethods.Offline,
                    booking.ReferenceCode,
                    $"{customer.LastName} {customer.FirstName}"));
            }
        }


        public async Task<Result<ProcessResult>> NotifyPaymentsNeeded(List<int> bookingIds)
        {
            var (_, isUserFailure, _, userError) = await _serviceAccountContext.GetUserInfo();
            if (isUserFailure)
                return Result.Fail<ProcessResult>(userError);

            var bookings = await GetBookings();

            return await Validate()
                .OnSuccess(ProcessBookings);


            Task<List<Booking>> GetBookings() => _context.Bookings.Where(booking => bookingIds.Contains(booking.Id)).ToListAsync();


            Result Validate()
            {
                return bookings.Count != bookingIds.Count
                    ? Result.Fail("Invalid booking ids. Could not find some of requested bookings.")
                    : Result.Combine(bookings.Select(ValidateBooking).ToArray());


                Result ValidateBooking(Booking booking)
                    => GenericValidator<Booking>.Validate(v =>
                    {
                        v.RuleFor(c => c.PaymentStatus)
                            .Must(status => booking.PaymentStatus == BookingPaymentStatuses.NotPaid)
                            .WithMessage(
                                $"Invalid payment status for booking '{booking.ReferenceCode}': {booking.PaymentStatus}");
                        v.RuleFor(c => c.Status)
                            .Must(status => BookingStatusesForPayment.Contains(status))
                            .WithMessage($"Invalid booking status for booking '{booking.ReferenceCode}': {booking.Status}");
                        v.RuleFor(c => c.PaymentMethod)
                            .Must(method => method == PaymentMethods.BankTransfer ||
                                method == PaymentMethods.CreditCard)
                            .WithMessage($"Invalid payment method for booking '{booking.ReferenceCode}': {booking.PaymentMethod}");
                    }, booking);
            }


            Task<ProcessResult> ProcessBookings()
            {
                return Combine(bookings.Select(ProcessBooking));


                Task<Result<string>> ProcessBooking(Booking booking)
                {
                    var bookingAvailability = JsonConvert.DeserializeObject<BookingAvailabilityInfo>(booking.ServiceDetails);
                    if (!Enum.TryParse<Currencies>(bookingAvailability.Agreement.Price.CurrencyCode, out var currency))
                        return Task.FromResult(Result.Fail<string>(
                            $"Unsupported currency in agreement: {bookingAvailability.Agreement.Price.CurrencyCode}"));

                    return Notify()
                        .OnBoth(CreateResult);


                    async Task<Result> Notify()
                    {
                        var customer = await _context.Customers.SingleOrDefaultAsync(c => c.Id == booking.CustomerId);
                        if (customer == default)
                            return Result.Fail($"Could not find customer with id {booking.CustomerId}");
                
                        return await _notificationService.SendNeedPaymentNotificationToCustomer(new PaymentBill(customer.Email,
                            bookingAvailability.Agreement.Price.NetTotal,
                            currency,
                            DateTime.MinValue, 
                            booking.PaymentMethod,
                            booking.ReferenceCode,
                            $"{customer.LastName} {customer.FirstName}"));
                    }


                    Result<string> CreateResult(Result result)
                        => result.IsSuccess
                            ? Result.Ok($"Payment for booking '{booking.ReferenceCode}' completed.")
                            : Result.Fail<string>($"Unable to complete payment for booking '{booking.ReferenceCode}'. Reason: {result.Error}");
                }
            }
        }


        private async Task<ProcessResult> Combine(IEnumerable<Task<Result<string>>> results)
        {
            var builder = new StringBuilder();

            foreach (var result in results)
            {
                var (_, isFailure, value, error) = await result;
                builder.AppendLine(isFailure ? error : value);
            }

            return new ProcessResult(builder.ToString());
        }


        private Task ChangeBookingPaymentStatusToCaptured(Booking booking)
        {
            booking.PaymentStatus = BookingPaymentStatuses.Captured;
            _context.Bookings.Update(booking);
            return _context.SaveChangesAsync();
        }


        private static PaymentResponse CreateResponse(CreditCardPaymentResult payment)
            => new PaymentResponse(payment.Secure3d, payment.Status, payment.Message);


        private async Task ChangePaymentStatusForBookingToAuthorized(CreditCardPaymentResult payment)
        {
            // Only when payment is completed
            if (payment.Status != PaymentStatuses.Success)
                return;

            // ReferenceCode should always contain valid booking reference code. We check it in CheckReferenceCode or StorePayment
            var booking = await _context.Bookings.FirstAsync(b => b.ReferenceCode == payment.ReferenceCode);
            await ChangePaymentStatusForBookingToAuthorized(payment, booking);
        }


        private async Task ChangePaymentStatusForBookingToAuthorized(CreditCardPaymentResult payment, Booking booking)
        {
            // Only when payment is completed
            if (payment.Status != PaymentStatuses.Success)
                return;

            booking.PaymentStatus = BookingPaymentStatuses.Authorized;
            _context.Update(booking);
            await _context.SaveChangesAsync();
        }


        private async Task MarkCreditCardAsUsed(CreditCardPaymentResult payment)
        {
            // Only when payment is completed
            if (payment.Status != PaymentStatuses.Success)
                return;

            var query = from booking in _context.Bookings
                join payments in _context.ExternalPayments on booking.Id equals payments.BookingId
                join cards in _context.CreditCards on payments.CreditCardId equals cards.Id
                where booking.ReferenceCode == payment.ReferenceCode
                select cards;

            // Maybe null for onetime tokens
            var card = await query.FirstOrDefaultAsync();
            if (card?.IsUsedForPayments != false)
                return;

            card.IsUsedForPayments = true;
            _context.Update(card);
            await _context.SaveChangesAsync();
        }


        private async Task<Result> Validate(PaymentRequest request, CustomerInfo customerInfo)
        {
            var fieldValidateResult = GenericValidator<PaymentRequest>.Validate(v =>
            {
                v.RuleFor(c => c.Amount).NotEmpty();
                v.RuleFor(c => c.Currency).NotEmpty().IsInEnum().Must(c => c != Common.Enums.Currencies.NotSpecified);
                v.RuleFor(c => c.ReferenceCode).NotEmpty();
                v.RuleFor(c => c.Token.Code).NotEmpty();
                v.RuleFor(c => c.Token.Type).NotEmpty().IsInEnum().Must(c => c != PaymentTokenTypes.Unknown);
                v.RuleFor(c => c.SecurityCode)
                    .Must(code => request.Token.Type == PaymentTokenTypes.OneTime || !string.IsNullOrEmpty(code))
                    .WithMessage("SecurityCode cannot be empty");
            }, request);

            if (fieldValidateResult.IsFailure)
                return fieldValidateResult;

            return Result.Combine(await CheckReferenceCode(request.ReferenceCode),
                await CheckToken());


            async Task<Result> CheckReferenceCode(string referenceCode)
            {
                var booking = await _context.Bookings.FirstOrDefaultAsync(b => b.ReferenceCode == referenceCode);
                if (booking == null)
                    return Result.Fail("Invalid Reference code");

                return GenericValidator<Booking>.Validate(v =>
                {
                    v.RuleFor(c => c.Status).Must(s => BookingStatusesForPayment.Contains(s))
                        .WithMessage($"Invalid booking status: {booking.Status.ToString()}");
                    v.RuleFor(c => c.PaymentMethod).Must(c => c == PaymentMethods.CreditCard)
                        .WithMessage($"Booking with reference code {booking.ReferenceCode} can be payed only with {booking.PaymentMethod.ToString()}");
                }, booking);
            }


            async Task<Result> CheckToken()
            {
                if (request.Token.Type != PaymentTokenTypes.Stored)
                    return Result.Ok();

                var token = request.Token.Code;
                var card = await _context.CreditCards.FirstOrDefaultAsync(c => c.Token == token);
                return await ChecksCreditCardExists()
                    .OnSuccess(CanUseCreditCard);

                Result ChecksCreditCardExists() => card != null ? Result.Ok() : Result.Fail("Cannot find a credit card by payment token");

                async Task<Result> CanUseCreditCard() => await _creditCardService.Get(card.Id, customerInfo);
            }
        }


        private async Task<Result<ExternalPayment>> GetPayment(Booking booking)
        {
            var payment = await _context.ExternalPayments.FirstOrDefaultAsync(p => p.BookingId == booking.Id);
            return payment == null
                ? Result.Fail<ExternalPayment>($"Cannot find external payment for booking '{booking.ReferenceCode}'")
                : Result.Ok(payment);
        }


        private static readonly Currencies[] Currencies = Enum.GetValues(typeof(Currencies))
            .Cast<Currencies>()
            .ToArray();

        private static readonly PaymentMethods[] AvailablePaymentMethods = {PaymentMethods.BankTransfer, PaymentMethods.CreditCard};

        private static readonly HashSet<BookingStatusCodes> BookingStatusesForPayment = new HashSet<BookingStatusCodes>
        {
            BookingStatusCodes.Pending, BookingStatusCodes.Confirmed
        };

        private static readonly HashSet<BookingStatusCodes> BookingStatusesForAuthorization = new HashSet<BookingStatusCodes>
        {
            BookingStatusCodes.Pending, BookingStatusCodes.Confirmed
        };

        private readonly IAccountManagementService _accountManagementService;
        private readonly ILogger<PaymentService> _logger;
        private readonly IAdministratorContext _adminContext;
        private readonly EdoContext _context;
        private readonly ICreditCardService _creditCardService;
        private readonly ICustomerContext _customerContext;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly IPaymentNotificationService _notificationService;
        private readonly IPayfortService _payfortService;
        private readonly IPaymentProcessingService _paymentProcessingService;
        private readonly IServiceAccountContext _serviceAccountContext;
    }
}