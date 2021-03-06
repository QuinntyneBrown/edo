using System.Threading.Tasks;
using CSharpFunctionalExtensions;
using HappyTravel.Edo.Api.Models.Agents;
using HappyTravel.Edo.Api.Models.Bookings;
using Microsoft.AspNetCore.Mvc;

namespace HappyTravel.Edo.Api.Services.Accommodations.Bookings
{
    public interface IBookingRegistrationService
    {
        Task<Result<string, ProblemDetails>> Register(AccommodationBookingRequest bookingRequest, AgentContext agentContext, string languageCode);

        Task<Result<AccommodationBookingInfo, ProblemDetails>> Finalize(string referenceCode, AgentContext agentContext, string languageCode);

        Task<Result<AccommodationBookingInfo, ProblemDetails>> BookByAccount(AccommodationBookingRequest bookingRequest,
            AgentContext agentContext, string languageCode, string clientIp);
    }
}