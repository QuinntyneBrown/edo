using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CSharpFunctionalExtensions;
using HappyTravel.Edo.Common.Enums;
using HappyTravel.Edo.UnitTests.Utility;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HappyTravel.Edo.UnitTests.Tests.AdministratorServices.CounterpartyManagementServiceTests
{
    public class CounterpartyVerificationTests
    {
        public CounterpartyVerificationTests()
        {
            _administratorServicesMockCreationHelper = new AdministratorServicesMockCreationHelper();
        }


        [Fact]
        public async Task Verification_of_not_existing_counterparty_as_full_accessed_should_fail()
        {
            var context = _administratorServicesMockCreationHelper.GetContextMock().Object;
            var counterpartyManagementService = _administratorServicesMockCreationHelper.GetCounterpartyManagementService(context);

            var (_, isFailure, error) = await counterpartyManagementService.VerifyAsFullyAccessed(7, "Test reason");

            Assert.True(isFailure);
        }


        [Fact]
        public async Task Verification_of_not_existing_counterparty_as_read_only_should_fail()
        {
            var context = _administratorServicesMockCreationHelper.GetContextMock().Object;
            var counterpartyManagementService = _administratorServicesMockCreationHelper.GetCounterpartyManagementService(context);

            var (_, isFailure, error) = await counterpartyManagementService.VerifyAsReadOnly(7, "Test reason");

            Assert.True(isFailure);
        }


        [Fact(Skip = "Will be fixed later, logic was changed")]
        public async Task Verification_as_full_accessed_should_update_counterparty_state()
        {
            var context = _administratorServicesMockCreationHelper.GetContextMock().Object;
            var counterpartyManagementService = _administratorServicesMockCreationHelper.GetCounterpartyManagementService(context);

            var (_, isFailure, error) = await counterpartyManagementService.VerifyAsFullyAccessed(1, "Test reason");

            var counterparty = context.Counterparties.Single(c => c.Id == 1);
            Assert.False(isFailure);
            Assert.True(counterparty.State == CounterpartyStates.FullAccess && counterparty.VerificationReason.Contains("Test reason"));
        }


        [Fact]
        public async Task Verification_as_read_only_should_update_counterparty_state()
        {
            var context = _administratorServicesMockCreationHelper.GetContextMock().Object;
            var counterpartyManagementService = _administratorServicesMockCreationHelper.GetCounterpartyManagementService(context);

            var (_, isFailure, error) = await counterpartyManagementService.VerifyAsReadOnly(1, "Test reason");

            var counterparty = context.Counterparties.Single(c => c.Id == 1);
            Assert.False(isFailure);
            Assert.True(counterparty.State == CounterpartyStates.ReadOnly && counterparty.VerificationReason.Contains("Test reason"));
        }


        [Fact]
        public async Task Verification_as_read_only_should_update_accounts()
        {
            var context = _administratorServicesMockCreationHelper.GetContextMock().Object;
            var counterpartyManagementService = _administratorServicesMockCreationHelper.GetCounterpartyManagementService(context);

            var (_, isFailure, error) = await counterpartyManagementService.VerifyAsReadOnly(1, "Test reason");

            var agencies = new List<int>() {1, 2};
            Assert.False(isFailure);
            Assert.True(context.CounterpartyAccounts.ToList().Count == 2);
            Assert.True(agencies.All(a => context.PaymentAccounts.Any(ac => ac.AgencyId == a)));
        }


        private readonly AdministratorServicesMockCreationHelper _administratorServicesMockCreationHelper;
    }
}