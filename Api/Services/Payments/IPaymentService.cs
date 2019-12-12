using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CSharpFunctionalExtensions;
using HappyTravel.Edo.Api.Models.Bookings;
using HappyTravel.Edo.Api.Models.Payments;
using HappyTravel.Edo.Api.Services.Customers;
using HappyTravel.Edo.Common.Enums;
using HappyTravel.Edo.Data.Booking;
using HappyTravel.EdoContracts.General.Enums;
using Newtonsoft.Json.Linq;

namespace HappyTravel.Edo.Api.Services.Payments
{
    public interface IPaymentService
    {
        IReadOnlyCollection<Currencies> GetCurrencies();
        IReadOnlyCollection<PaymentMethods> GetAvailableCustomerPaymentMethods();
        Task<Result> ReplenishAccount(int accountId, PaymentData payment);
        Task<Result<PaymentResponse>> Pay(PaymentRequest request, string languageCode, string ipAddress, CustomerInfo customerInfo);
        Task<Result<PaymentResponse>> ProcessPaymentResponse(JObject response);
        Task<bool> CanPayWithAccount(CustomerInfo customerInfo);
        Task<Result<List<int>>> GetBookingsForCapture(DateTime deadlineDate);
        Task<Result<ProcessResult>> CaptureMoneyForBookings(List<int> bookingIds);
        Task<Result> AuthorizeMoneyFromAccount(Booking booking, CustomerInfo customerInfo);
        Task<Result> VoidMoney(Booking booking);
        Task<Result> CompleteOffline(int bookingId);
        Task<Result<ProcessResult>> NotifyPaymentsNeeded(List<int> bookingIds);
    }
}