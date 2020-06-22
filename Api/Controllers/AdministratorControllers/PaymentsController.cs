using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using HappyTravel.Edo.Api.Filters.Authorization.AdministratorFilters;
using HappyTravel.Edo.Api.Infrastructure;
using HappyTravel.Edo.Api.Models.Management.Enums;
using HappyTravel.Edo.Api.Models.Payments;
using HappyTravel.Edo.Api.Services.Accommodations.Bookings;
using HappyTravel.Edo.Api.Services.Management;
using HappyTravel.Edo.Api.Services.Payments.Accounts;
using HappyTravel.Money.Enums;
using HappyTravel.Money.Models;
using Microsoft.AspNetCore.Mvc;
using CSharpFunctionalExtensions;
using HappyTravel.Edo.Api.Models.Users;
using HappyTravel.EdoContracts.General.Enums;

namespace HappyTravel.Edo.Api.Controllers.AdministratorControllers
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/{v:apiVersion}/admin/payments")]
    [Produces("application/json")]
    public class PaymentsController : BaseController
    {
        public PaymentsController(IAccountPaymentService accountPaymentService, IBookingPaymentService bookingPaymentService,
            IAdministratorContext administratorContext, ICounterpartyAccountService counterpartyAccountService)
        {
            _accountPaymentService = accountPaymentService;
            _bookingPaymentService = bookingPaymentService;
            _administratorContext = administratorContext;
            _counterpartyAccountService = counterpartyAccountService;
        }


        /// <summary>	
        ///     Appends money to specified account.	
        /// </summary>	
        /// <param name="accountId">Id of account to add money.</param>	
        /// <param name="paymentData">Payment details.</param>	
        /// <returns></returns>	
        [HttpPost("{accountId}/replenish")]
        [ProducesResponseType(typeof(IReadOnlyCollection<PaymentMethods>), (int) HttpStatusCode.NoContent)]
        [AdministratorPermissions(AdministratorPermissions.AccountReplenish)]
        public async Task<IActionResult> ReplenishAccount(int accountId, [FromBody] PaymentData paymentData)
        {
            var (_, _, administrator, _) = await _administratorContext.GetCurrent();
            var (isSuccess, _, error) = await _accountPaymentService.ReplenishAccount(accountId, paymentData, administrator);
            return isSuccess
                ? NoContent()
                : (IActionResult) BadRequest(ProblemDetailsBuilder.Build(error));
        }


        /// <summary>
        ///     Completes payment manually
        /// </summary>
        /// <param name="bookingId">Booking id for completion</param>
        [HttpPost("offline/{bookingId}")]
        [ProducesResponseType((int) HttpStatusCode.NoContent)]
        [ProducesResponseType(typeof(ProblemDetails), (int) HttpStatusCode.BadRequest)]
        [AdministratorPermissions(AdministratorPermissions.OfflinePayment)]
        public async Task<IActionResult> CompleteOffline(int bookingId)
        {
            var (_, _, administrator, _) = await _administratorContext.GetCurrent();
            var (_, isFailure, error) = await _bookingPaymentService.CompleteOffline(bookingId, administrator);
            if (isFailure)
                return BadRequest(ProblemDetailsBuilder.Build(error));

            return NoContent();
        }


        /// <summary>
        ///     Gets balance for a counterparty account
        /// </summary>
        /// <param name="counterpartyId">Id of the counterparty</param>
        /// <param name="currency">Currency</param>
        [HttpGet("counterparties/{counterpartyId}/balance/{currency}")]
        [ProducesResponseType(typeof(CounterpartyBalanceInfo), (int) HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ProblemDetails), (int) HttpStatusCode.BadRequest)]
        [AdministratorPermissions(AdministratorPermissions.CounterpartyBalanceObservation)]
        public async Task<IActionResult> GetCounterpartyBalance(int counterpartyId, Currencies currency)
            => OkOrBadRequest(await _counterpartyAccountService.GetBalance(counterpartyId, currency));


        /// <summary>
        ///     Appends money to a counterparty account
        /// </summary>
        /// <param name="counterpartyAccountId">Id of the counterparty account</param>
        /// <param name="paymentData">Details about the payment</param>
        [HttpPost("counterparty-accounts/{counterpartyAccountId}/replenish")]
        [ProducesResponseType((int) HttpStatusCode.NoContent)]
        [ProducesResponseType(typeof(ProblemDetails), (int) HttpStatusCode.BadRequest)]
        [AdministratorPermissions(AdministratorPermissions.CounterpartyBalanceReplenishAndSubtract)]
        public async Task<IActionResult> ReplenishCounterpartyAccount(int counterpartyAccountId, [FromBody] PaymentData paymentData)
        {
            var (_, _, administrator, _) = await _administratorContext.GetCurrent();
            var (isSuccess, _, error) = await _counterpartyAccountService.AddMoney(counterpartyAccountId, paymentData,
                administrator.ToUserInfo());

            return isSuccess
                ? NoContent()
                : (IActionResult) BadRequest(ProblemDetailsBuilder.Build(error));
        }


        /// <summary>
        ///     Subtracts money from a counterparty account due to payment cancellation
        /// </summary>
        /// <param name="counterpartyAccountId">Id of the counterparty account</param>
        /// <param name="cancellationData">Details about the payment cancellation</param>
        [HttpPost("counterparty-accounts/{counterpartyAccountId}/subtract")]
        [ProducesResponseType((int) HttpStatusCode.NoContent)]
        [ProducesResponseType(typeof(ProblemDetails), (int) HttpStatusCode.BadRequest)]
        [AdministratorPermissions(AdministratorPermissions.CounterpartyBalanceReplenishAndSubtract)]
        public async Task<IActionResult> SubtractCounterpartyAccount(int counterpartyAccountId, [FromBody] PaymentCancellationData cancellationData)
        {
            var (_, _, administrator, _) = await _administratorContext.GetCurrent();
            var (isSuccess, _, error) = await _counterpartyAccountService.SubtractMoney(counterpartyAccountId,
                cancellationData, administrator.ToUserInfo());

            return isSuccess
                ? NoContent()
                : (IActionResult) BadRequest(ProblemDetailsBuilder.Build(error));
        }


        /// <summary>
        ///     Transfers money from a counterparty account to the default agency account
        /// </summary>
        /// <param name="counterpartyAccountId">Id of the counterparty account</param>
        /// <param name="amount">Amount of money to transfer</param>
        [HttpPost("counterparty-accounts/{counterpartyAccountId}/transfer")]
        [ProducesResponseType((int) HttpStatusCode.NoContent)]
        [ProducesResponseType(typeof(ProblemDetails), (int) HttpStatusCode.BadRequest)]
        [AdministratorPermissions(AdministratorPermissions.CounterpartyToAgencyTransfer)]
        public async Task<IActionResult> TransferToDefaultAgency(int counterpartyAccountId, [FromBody] MoneyAmount amount)
        {
            var (_, _, administrator, _) = await _administratorContext.GetCurrent();
            var (isSuccess, _, error) = await _counterpartyAccountService.TransferToDefaultAgency(counterpartyAccountId, amount,
                administrator.ToUserInfo());

            return isSuccess
                ? NoContent()
                : (IActionResult) BadRequest(ProblemDetailsBuilder.Build(error));
        }


        private readonly IAccountPaymentService _accountPaymentService;
        private readonly IAdministratorContext _administratorContext;
        private readonly IBookingPaymentService _bookingPaymentService;
        private readonly ICounterpartyAccountService _counterpartyAccountService;
    }
}