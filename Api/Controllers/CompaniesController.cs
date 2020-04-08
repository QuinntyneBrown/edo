using System.Net;
using System.Threading.Tasks;
using HappyTravel.Edo.Api.Filters.Authorization.AdministratorFilters;
using HappyTravel.Edo.Api.Filters.Authorization.CompanyStatesFilters;
using HappyTravel.Edo.Api.Filters.Authorization.InCompanyPermissionFilters;
using HappyTravel.Edo.Api.Infrastructure;
using HappyTravel.Edo.Api.Models.Branches;
using HappyTravel.Edo.Api.Models.Customers;
using HappyTravel.Edo.Api.Models.Management;
using HappyTravel.Edo.Api.Models.Management.Enums;
using HappyTravel.Edo.Api.Services.Customers;
using HappyTravel.Edo.Common.Enums;
using Microsoft.AspNetCore.Mvc;

namespace HappyTravel.Edo.Api.Controllers
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/{v:apiVersion}/companies")]
    [Produces("application/json")]
    public class CompaniesController : BaseController
    {
        public CompaniesController(ICompanyService companyService)
        {
            _companyService = companyService;
        }


        /// <summary>
        ///     Sets company fully verified.
        /// </summary>
        /// <param name="companyId">Id of the company to verify.</param>
        /// <param name="request">Verification details.</param>
        /// <returns></returns>
        [HttpPost("{companyId}/verify")]
        [ProducesResponseType((int) HttpStatusCode.NoContent)]
        [ProducesResponseType(typeof(ProblemDetails), (int) HttpStatusCode.BadRequest)]
        [AdministratorPermissions(AdministratorPermissions.CompanyVerification)]
        public async Task<IActionResult> Verify(int companyId, [FromBody] CompanyVerificationRequest request)
        {
            var (isSuccess, _, error) = await _companyService.VerifyAsFullyAccessed(companyId, request.Reason);

            return isSuccess
                ? (IActionResult) NoContent()
                : BadRequest(ProblemDetailsBuilder.Build(error));
        }


        /// <summary>
        ///     Sets company read-only verified.
        /// </summary>
        /// <param name="companyId">Id of the company to verify.</param>
        /// <param name="request">Verification details.</param>
        /// <returns></returns>
        [HttpPost("{companyId}/verify/read-only")]
        [ProducesResponseType((int) HttpStatusCode.NoContent)]
        [ProducesResponseType(typeof(ProblemDetails), (int) HttpStatusCode.BadRequest)]
        [AdministratorPermissions(AdministratorPermissions.CompanyVerification)]
        public async Task<IActionResult> VerifyAsReadOnly(int companyId, [FromBody] CompanyVerificationRequest request)
        {
            var (isSuccess, _, error) = await _companyService.VerifyAsReadOnly(companyId, request.Reason);

            return isSuccess
                ? (IActionResult) NoContent()
                : BadRequest(ProblemDetailsBuilder.Build(error));
        }


        /// <summary>
        ///     Creates branch for company.
        /// </summary>
        /// <param name="companyId">Company Id.</param>
        /// <param name="branchInfo">Branch information.</param>
        /// <returns></returns>
        [HttpPost("{companyId}/branches")]
        [ProducesResponseType((int)HttpStatusCode.NoContent)]
        [ProducesResponseType(typeof(ProblemDetails), (int)HttpStatusCode.BadRequest)]
        public async Task<IActionResult> AddBranch(int companyId, [FromBody] BranchInfo branchInfo)
        {
            var (isSuccess, _, _, error) = await _companyService.AddBranch(companyId, branchInfo);

            return isSuccess
                ? (IActionResult)NoContent()
                : BadRequest(ProblemDetailsBuilder.Build(error));
        }


        /// <summary>
        ///     Gets branch.
        /// </summary>
        /// <param name="companyId">Company Id.</param>
        /// <param name="branchId">Branch Id.</param>
        /// <returns></returns>
        [HttpGet("{companyId}/branches/{branchId}")]
        [ProducesResponseType((int)HttpStatusCode.NoContent)]
        [ProducesResponseType(typeof(ProblemDetails), (int)HttpStatusCode.BadRequest)]
        public async Task<IActionResult> GetBranch(int companyId, int branchId)
        {
            var (_, isFailure, branch, error) = await _companyService.GetBranch(companyId, branchId);

            if (isFailure)
                return BadRequest(ProblemDetailsBuilder.Build(error));

            return Ok(branch);
        }


        /// <summary>
        ///     Gets all branches of a company.
        /// </summary>
        /// <param name="companyId">Company Id.</param>
        /// <returns></returns>
        [HttpGet("{companyId}/branches")]
        [ProducesResponseType((int)HttpStatusCode.NoContent)]
        [ProducesResponseType(typeof(ProblemDetails), (int)HttpStatusCode.BadRequest)]
        public async Task<IActionResult> GetBranches(int companyId)
        {
            var (_, isFailure, branch, error) = await _companyService.GetAllCompanyBranches(companyId);
            if (isFailure)
                return BadRequest(ProblemDetailsBuilder.Build(error));

            return Ok(branch);
        }


        /// <summary>
        ///     Updates company information.
        /// </summary>
        /// <param name="companyId">Id of the company to verify.</param>
        /// <param name="updatedCompanyInfo">New company information.</param>
        /// <returns></returns>
        [HttpPut("{companyId}")]
        [ProducesResponseType(typeof(CompanyInfo), (int) HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ProblemDetails), (int) HttpStatusCode.BadRequest)]
        [MinCompanyState(CompanyStates.ReadOnly)]
        [InCompanyPermissions(InCompanyPermissions.EditCompanyInfo)]
        public async Task<IActionResult> UpdateCompany(int companyId, [FromBody] CompanyInfo updatedCompanyInfo)
        {
            var (_, isFailure, savedCompanyInfo, error) = await _companyService.Update(updatedCompanyInfo, companyId);
            
            if (isFailure)
                return BadRequest(ProblemDetailsBuilder.Build(error));

            return Ok(savedCompanyInfo);
        }

        /// <summary>
        ///     Gets company information.
        /// </summary>
        /// <param name="companyId">Id of the company to verify.</param>
        /// <returns></returns>
        [HttpGet("{companyId}")]
        [ProducesResponseType(typeof(CompanyInfo), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ProblemDetails), (int)HttpStatusCode.BadRequest)]
        public async Task<IActionResult> GetCompany(int companyId)
        {
            var (_, isFailure, companyInfo, error) = await _companyService.Get(companyId);

            if (isFailure)
                return BadRequest(ProblemDetailsBuilder.Build(error));

            return Ok(companyInfo);
        }

        private readonly ICompanyService _companyService;
    }
}