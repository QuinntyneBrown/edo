using System.Collections.Generic;
using HappyTravel.EdoContracts.Accommodations.Internals;

namespace HappyTravel.Edo.Api.Models.Accommodations
{
    public readonly struct AvailabilityResult
    {
        public AvailabilityResult(string availabilityId, SlimAccommodationDetails accommodationDetails, List<Agreement> agreements)
        {
            AvailabilityId = availabilityId;
            AccommodationDetails = accommodationDetails;
            Agreements = agreements ?? new List<Agreement>(0);
        }


        public AvailabilityResult(AvailabilityResult result, List<Agreement> agreements)
            : this(result.AvailabilityId, result.AccommodationDetails, agreements)
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
        /// List of available agreements
        /// </summary>
        public List<Agreement> Agreements { get; }
    }
}