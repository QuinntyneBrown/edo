using System.Linq;
using System.Threading.Tasks;
using CSharpFunctionalExtensions;
using HappyTravel.Edo.Api.Infrastructure;
using HappyTravel.Edo.Api.Infrastructure.FunctionalExtensions;
using HappyTravel.Edo.Api.Infrastructure.Logging;
using HappyTravel.Edo.Api.Models.Management.AuditEvents;
using HappyTravel.Edo.Api.Services.Management;
using HappyTravel.Edo.Common.Enums;
using HappyTravel.Edo.Data;
using HappyTravel.Edo.Data.Agents;
using HappyTravel.Edo.Data.Payments;
using HappyTravel.Money.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HappyTravel.Edo.Api.Services.Payments.Accounts
{
    public class AccountManagementService : IAccountManagementService
    {
        public AccountManagementService(EdoContext context,
            IDateTimeProvider dateTimeProvider,
            ILogger<AccountManagementService> logger,
            IAdministratorContext administratorContext,
            IManagementAuditService managementAuditService,
            IEntityLocker locker)
        {
            _context = context;
            _dateTimeProvider = dateTimeProvider;
            _logger = logger;
            _administratorContext = administratorContext;
            _managementAuditService = managementAuditService;
            _locker = locker;
        }


        public async Task<Result> CreateForAgency(Agency agency, Currencies currency)
        {
            return await Result.Ok()
                .Ensure(IsCounterpartyVerified, "Account creation is only available for verified counterparties")
                .Map(CreateAccount)
                .Tap(LogSuccess)
                .OnFailure(LogFailure);


            async Task<bool> IsCounterpartyVerified()
            {
                var counterparty = await _context.Counterparties.Where(c => c.Id == agency.CounterpartyId).SingleAsync();
                return new[] {CounterpartyStates.ReadOnly, CounterpartyStates.FullAccess}.Contains(counterparty.State);
            }


            async Task<PaymentAccount> CreateAccount()
            {
                var account = new PaymentAccount
                {
                    Balance = 0,
                    CreditLimit = 0,
                    AgencyId = agency.Id,
                    Currency = Currencies.USD, // Only USD currency is supported
                    Created = _dateTimeProvider.UtcNow()
                };
                _context.PaymentAccounts.Add(account);
                await _context.SaveChangesAsync();

                return account;
            }


            void LogSuccess(PaymentAccount account)
            {
                _logger.LogPaymentAccountCreationSuccess(
                    $"Successfully created payment account for agency: '{agency.Id}', account id: {account.Id}");
            }


            void LogFailure(string error)
            {
                _logger.LogPaymentAccountCreationFailed($"Failed to create account for agency {agency.Id}, error {error}");
            }
        }


        public async Task<Result> CreateForCounterparty(Counterparty counterparty, Currencies currency)
        {
            return await Result.Ok()
                .Ensure(IsCounterpartyVerified, "Account creation is only available for verified counterparties")
                .Map(CreateAccount)
                .Tap(LogSuccess)
                .OnFailure(LogFailure);

            
            bool IsCounterpartyVerified() =>
                new[] { CounterpartyStates.ReadOnly, CounterpartyStates.FullAccess }.Contains(counterparty.State);


            async Task<CounterpartyAccount> CreateAccount()
            {
                var account = new CounterpartyAccount
                {
                    Balance = 0,
                    CounterpartyId = counterparty.Id,
                    Currency = Currencies.USD, // Only USD currency is supported
                    Created = _dateTimeProvider.UtcNow()
                };
                _context.CounterpartyAccounts.Add(account);
                await _context.SaveChangesAsync();

                return account;
            }


            void LogSuccess(CounterpartyAccount account)
            {
                _logger.LogCounterpartyAccountCreationSuccess(
                    $"Successfully created counterparty account for counterparty: '{counterparty.Id}', account id: {account.Id}");
            }


            void LogFailure(string error)
            {
                _logger.LogCounterpartyAccountCreationFailed($"Failed to create account for counterparty {counterparty.Id}, error {error}");
            }
        }


        public Task<Result> ChangeCreditLimit(int accountId, decimal creditLimit)
        {
            return Result.Ok()
                .Ensure(CreditLimitIsValid, "Credit limit should be greater than zero")
                .Bind(GetAccount)
                .Bind(LockAccount)
                .BindWithTransaction(_context, account => Result.Ok(account)
                    .Bind(UpdateCreditLimit)
                    .Bind(WriteAuditLog))
                .Finally(UnlockAccount);


            async Task<Result<PaymentAccount>> LockAccount(PaymentAccount account)
            {
                var (isSuccess, _, error) = await _locker.Acquire<PaymentAccount>(account.Id.ToString(), nameof(IAccountPaymentProcessingService));
                return isSuccess
                    ? Result.Ok(account)
                    : Result.Failure<PaymentAccount>(error);
            }


            async Task<Result> UnlockAccount(Result accountResult)
            {
                await _locker.Release<PaymentAccount>(accountId.ToString());
                return accountResult;
            }


            async Task<Result<PaymentAccount>> GetAccount()
            {
                var account = await _context.PaymentAccounts.SingleOrDefaultAsync(p => p.Id == accountId);
                return account == default
                    ? Result.Failure<PaymentAccount>("Could not find payment account")
                    : Result.Ok(account);
            }


            bool CreditLimitIsValid() => creditLimit >= 0;


            async Task<Result<(decimal creditLimitBefore, decimal creditLimitAfter)>> UpdateCreditLimit(PaymentAccount account)
            {
                var currentCreditLimit = account.CreditLimit;
                account.CreditLimit = creditLimit;
                _context.Update(account);
                await _context.SaveChangesAsync();
                return Result.Ok((currentCreditLimit, creditLimit));
            }


            Task<Result> WriteAuditLog((decimal creditLimitBefore, decimal creditLimitAfter) limitChanges)
                => _managementAuditService.Write(ManagementEventType.AccountCreditLimitChange,
                    new AccountCreditLimitChangeEvent(accountId, limitChanges.creditLimitBefore, limitChanges.creditLimitAfter));
        }


        public async Task<Result<PaymentAccount>> Get(int agencyId, Currencies currency)
        {
            var account = await _context.PaymentAccounts.FirstOrDefaultAsync(a => a.AgencyId == agencyId && a.Currency == currency);
            return account == null
                ? Result.Failure<PaymentAccount>($"Cannot find payment account for agency '{agencyId}' and currency '{currency}'")
                : Result.Ok(account);
        }


        private readonly IAdministratorContext _administratorContext;
        private readonly EdoContext _context;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly IEntityLocker _locker;
        private readonly ILogger<AccountManagementService> _logger;
        private readonly IManagementAuditService _managementAuditService;
    }
}