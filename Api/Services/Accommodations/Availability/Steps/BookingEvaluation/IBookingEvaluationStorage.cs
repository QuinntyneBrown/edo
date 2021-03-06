using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CSharpFunctionalExtensions;
using HappyTravel.Edo.Api.Models.Markups;
using HappyTravel.Edo.Common.Enums;
using HappyTravel.EdoContracts.Accommodations;

namespace HappyTravel.Edo.Api.Services.Accommodations.Availability.Steps.BookingEvaluation
{
    public interface IBookingEvaluationStorage
    {
        Task Set(Guid searchId, Guid resultId, Guid roomContractSetId, DataWithMarkup<RoomContractSetAvailability> availability, Suppliers resultSupplier);
        
        Task<Result<(Suppliers Source, DataWithMarkup<RoomContractSetAvailability> Result)>> Get(Guid searchId, Guid resultId, Guid roomContractSetId, List<Suppliers> suppliers);
    }
}