﻿using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using HappyTravel.Edo.Api.Models.Locations;
using HappyTravel.Edo.Api.Services.Locations;
using HappyTravel.Edo.Common.Enums;
using Microsoft.AspNetCore.Mvc;

namespace HappyTravel.Edo.Api.Controllers
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/{v:apiVersion}/locations")]
    [Produces("application/json")]
    public class LocationsController : ControllerBase
    {
        public LocationsController(ILocationService service)
        {
            _service = service;
        }


        /// <summary>
        /// Returns a list of world countries.
        /// </summary>
        /// <param name="languageCode"></param>
        /// <param name="query">The search query text.</param>
        /// <returns></returns>
        [HttpGet("countries")]
        [ProducesResponseType(typeof(List<Country>), (int)HttpStatusCode.OK)]
        public async Task<IActionResult> GetCountries([FromQuery] string languageCode, [FromQuery] string query) 
            => Ok(await _service.GetCountries(query, languageCode));


        /// <summary>
        /// Returns location predictions what a used when searching 
        /// </summary>
        /// <param name="languageCode"></param>
        /// <param name="query">The search query text.</param>
        /// <param name="session">The search session ID.</param>
        /// <returns></returns>
        [HttpGet("predictions")]
        [ProducesResponseType(typeof(List<Prediction>), (int)HttpStatusCode.OK)]
        public async Task<IActionResult> GetLocationPredictions([FromQuery] string languageCode, [FromQuery] string query, [FromQuery][Required] string session)
        {
            var (_, isFailure, value, error) = await _service.GetPredictions(query, session, languageCode);
            return isFailure 
                ? (IActionResult) BadRequest(error) 
                : Ok(value);
        }


        /// <summary>
        /// Returns a list of world regions.
        /// </summary>
        /// <param name="languageCode"></param>
        /// <returns></returns>
        [HttpGet("regions")]
        [ProducesResponseType(typeof(List<Region>), (int)HttpStatusCode.OK)]
        public async Task<IActionResult> GetRegions([FromQuery] string languageCode)
            => Ok(await _service.GetRegions(languageCode));


        /// <summary>
        /// Internal. Sets locations, gathered from booking sources, to make predictions.
        /// </summary>
        /// <param name="source">The source of location data.</param>
        /// <param name="locations"></param>
        /// <returns></returns>
        [ProducesResponseType((int)HttpStatusCode.NoContent)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [HttpPost("{source}")]
        public async ValueTask<IActionResult> SetPredictions([FromRoute] PredictionSources source, [FromBody] IEnumerable<Location> locations)
        {
            if (locations is null || !locations.Any())
                return NoContent();

            await _service.Set(source, locations);
            return NoContent();
        }


        private readonly ILocationService _service;
    }
}
