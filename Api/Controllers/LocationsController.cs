﻿using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using HappyTravel.Edo.Api.Models.Locations;
using HappyTravel.Edo.Api.Services.Data;
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
        /// Returns a list of world regions.
        /// </summary>
        /// <param name="languageCode"></param>
        /// <returns></returns>
        [HttpGet("regions")]
        [ProducesResponseType(typeof(List<Region>), (int)HttpStatusCode.OK)]
        public async Task<IActionResult> GetRegions([FromQuery] string languageCode)
            => Ok(await _service.GetRegions(languageCode));


        private readonly ILocationService _service;
    }
}
