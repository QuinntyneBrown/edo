using System.Collections.Generic;
using HappyTravel.EdoContracts.Accommodations.Internals;

namespace HappyTravel.Edo.Api.Models.Accommodations
{
    public readonly struct AvailabilityResult
    {
        public AvailabilityResult(string availabilityId, SlimAccommodationDetails accommodationDetails, List<RoomContractSet> roomContractSets)
        {
            AvailabilityId = availabilityId;
            AccommodationDetails = accommodationDetails;
            RoomContractSets = roomContractSets ?? new List<RoomContractSet>(0);
        }


        public AvailabilityResult(AvailabilityResult result, List<RoomContractSet> roomContractSets)
            : this(result.AvailabilityId, result.AccommodationDetails, roomContractSets)
        { }
        
        /// <summary>
        /// Id of availability search
        /// </summary>
        public string AvailabilityId { get; }
        
        /// <summary>
        /// Accommodation data
        /// </summary>
        public SlimAccommodationDetails AccommodationDetails { get; }
        
        /// <summary>
        /// List of available room contracts sets
        /// </summary>
        public List<RoomContractSet> RoomContractSets { get; }
    }
}