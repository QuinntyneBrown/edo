using System.Threading.Tasks;
using CSharpFunctionalExtensions;
using HappyTravel.Edo.Api.Models.Agents;
using HappyTravel.Edo.Api.Models.Payments;
using HappyTravel.Edo.Data.Booking;
using HappyTravel.EdoContracts.General;
using Newtonsoft.Json.Linq;

namespace HappyTravel.Edo.Api.Services.Payments.CreditCards
{
    public interface ICreditCardPaymentService
    {
        Task<Result<string>> CaptureMoney(Booking booking);
        Task<Result> VoidMoney(Booking booking);
        Task<Result<Price>> GetPendingAmount(Booking booking);
        Task<Result<PaymentResponse>> AuthorizeMoney(NewCreditCardBookingPaymentRequest request, string languageCode, string ipAddress);
        Task<Result<PaymentResponse>> AuthorizeMoney(SavedCreditCardBookingPaymentRequest request, string languageCode, string ipAddress);
        Task<Result<PaymentResponse>> ProcessPaymentResponse(JObject response);
    }
}