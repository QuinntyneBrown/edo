using System.Threading.Tasks;
using CSharpFunctionalExtensions;
using HappyTravel.Edo.Api.Models.Payments.External;

namespace HappyTravel.Edo.Api.Services.PaymentLinks
{
    public interface IPaymentLinkService
    {
        Task<Result> SendLink(string email, PaymentLinkData paymentLinkData);
        PaymentLinkSettings GetSettings();
    }
}