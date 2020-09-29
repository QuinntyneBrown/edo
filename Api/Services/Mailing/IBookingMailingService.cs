﻿using System.Threading.Tasks;
using CSharpFunctionalExtensions;
using HappyTravel.Edo.Api.Models.Agents;
using HappyTravel.Edo.Api.Models.Bookings;

namespace HappyTravel.Edo.Api.Services.Mailing
{
    public interface IBookingMailingService
    {
        Task<Result> SendVoucher(int bookingId, string email, AgentContext agent, string languageCode);

        Task<Result> SendInvoice(int bookingId, string email, AgentContext agent, string languageCode);

        Task<Result> NotifyBookingCancelled(string referenceCode, string email, string agentName);

        Task NotifyBookingFinalized(in AccommodationBookingInfo bookingInfo, in AgentContext agentContext);

        Task<Result> NotifyDeadlineApproaching(int bookingId, string email);

        Task SendBookingReports(int agencyId);
    }
}