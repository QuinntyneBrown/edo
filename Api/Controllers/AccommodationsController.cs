﻿using System.Net;
using System.Threading.Tasks;
using HappyTravel.Edo.Api.Infrastructure;
using HappyTravel.Edo.Api.Models.Accommodations;
using HappyTravel.Edo.Api.Models.Availabilities;
using HappyTravel.Edo.Api.Models.Bookings;
using HappyTravel.Edo.Api.Services.Accommodations;
using Microsoft.AspNetCore.Mvc;

namespace HappyTravel.Edo.Api.Controllers
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/{v:apiVersion}")]
    [Produces("application/json")]
    public class AccommodationsController : BaseController
    {
        public AccommodationsController(IAccommodationService service)
        {
            _service = service;
        }


        /// <summary>
        ///     Returns the full set of accommodation details.
        /// </summary>
        /// <param name="accommodationId">Accommodation ID, obtained from an availability query.</param>
        /// <returns></returns>
        [HttpGet("accommodations/{accommodationId}")]
        [ProducesResponseType(typeof(RichAccommodationDetails), (int) HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ProblemDetails), (int) HttpStatusCode.BadRequest)]
        public async ValueTask<IActionResult> Get([FromRoute] string accommodationId)
        {
            if (string.IsNullOrWhiteSpace(accommodationId))
                return BadRequest(ProblemDetailsBuilder.Build("No accommodation IDs was provided."));

            var (_, isFailure, response, error) = await _service.Get(accommodationId, LanguageCode);
            if (isFailure)
                return BadRequest(error);

            return Ok(response);
        }


        /// <summary>
        /// Returns accommodations available for a booking.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [HttpPost("availabilities/accommodations")]
        [ProducesResponseType(typeof(AvailabilityResponse), (int)HttpStatusCode.OK)]
        public async Task<IActionResult> GetAvailability([FromBody] AvailabilityRequest request)
        {
            var (_, isFailure, response, error) = await _service.GetAvailable(request, LanguageCode);
            if (isFailure)
                return BadRequest(error);

            return Ok(response);
        }


        /// <summary>
        ///     Book an accommodation.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [HttpPost("bookings/accommodations")]
        [ProducesResponseType(typeof(AccommodationBookingDetails), (int) HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ProblemDetails), (int) HttpStatusCode.BadRequest)]
        public async Task<IActionResult> Book([FromBody] AccommodationBookingRequest request)
        {
            var (_, isFailure, bookingDetails, error) = await _service.Book(request, LanguageCode);
            if (isFailure)
                return BadRequest(error);

            return Ok(bookingDetails);
        }


        private readonly IAccommodationService _service;
    }
}