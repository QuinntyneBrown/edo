﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CSharpFunctionalExtensions;
using HappyTravel.Edo.Api.Infrastructure;
using HappyTravel.Edo.Api.Models.Agents;
using HappyTravel.Edo.Api.Services.Agents;
using HappyTravel.Edo.Api.Services.Payments.Accounts;
using HappyTravel.Edo.Common.Enums;
using HappyTravel.Edo.Data;
using HappyTravel.Edo.Data.Agents;
using HappyTravel.Edo.Data.Payments;
using HappyTravel.Edo.UnitTests.Infrastructure;
using HappyTravel.Edo.UnitTests.Infrastructure.DbSetMocks;
using HappyTravel.Money.Enums;
using HappyTravel.Money.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using Xunit;

namespace HappyTravel.Edo.UnitTests.Payments.PaymentAccounts
{
    public class TransferMoneyTests
    {

        public TransferMoneyTests(Mock<EdoContext> edoContextMock)
        {
            var entityLockerMock = new Mock<IEntityLocker>();

            entityLockerMock.Setup(l => l.Acquire<It.IsAnyType>(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.FromResult(Result.Ok()));

            _edoContextMock = edoContextMock;
            _mockedEdoContext = edoContextMock.Object;

            _agentContext = new Mock<IAgentContextService>(); // Setup in SetupInitialData() or in tests

            var accountPaymentProcessingService = new AccountPaymentProcessingService(
                _mockedEdoContext, entityLockerMock.Object, Mock.Of<IAccountBalanceAuditService>());

            _accountPaymentService = new AccountPaymentService(
                accountPaymentProcessingService, _mockedEdoContext, Mock.Of<IDateTimeProvider>(),
                _agentContext.Object, Mock.Of<IAccountManagementService>());

            var strategy = new ExecutionStrategyMock();

            var dbFacade = new Mock<DatabaseFacade>(_mockedEdoContext);
            dbFacade.Setup(d => d.CreateExecutionStrategy()).Returns(strategy);
            edoContextMock.Setup(c => c.Database).Returns(dbFacade.Object);
        }


        [Fact]
        public async Task Nonexistent_payer_account_should_fail()
        {
            SetupInitialData();

            var (_, isFailure, error) = await _accountPaymentService.TransferToChildAgency(0, 2, new MoneyAmount(1m, Currencies.USD));

            Assert.True(isFailure);
        }


        [Fact]
        public async Task Nonexistent_recipient_account_should_fail()
        {
            SetupInitialData();

            var (_, isFailure, error) = await _accountPaymentService.TransferToChildAgency(1, 0, new MoneyAmount(1m, Currencies.USD));

            Assert.True(isFailure);
        }


        [Fact]
        public async Task Agent_transfer_from_different_agency_should_fail()
        {
            SetupInitialData();
            SetAgencyIdForAgent(2);

            var (_, isFailure, error) = await _accountPaymentService.TransferToChildAgency(1, 2, new MoneyAmount(1m, Currencies.USD));

            Assert.True(isFailure);
        }


        [Fact]
        public async Task Negative_amount_transfer_should_fail()
        {
            SetupInitialData();

            var (_, isFailure, error) = await _accountPaymentService.TransferToChildAgency(1, 2, new MoneyAmount(-1m, Currencies.USD));

            Assert.True(isFailure);
        }


        [Fact]
        public async Task Transfer_to_not_child_agency_should_fail()
        {
            SetupInitialData();

            var (_, isFailure, error) = await _accountPaymentService.TransferToChildAgency(1, 3, new MoneyAmount(1m, Currencies.USD));

            Assert.True(isFailure);
        }


        [Fact]
        public async Task Transfer_to_account_with_different_currency_should_fail()
        {
            SetupInitialData();

            var (_, isFailure, error) = await _accountPaymentService.TransferToChildAgency(1, 4, new MoneyAmount(1m, Currencies.USD));

            Assert.True(isFailure);
        }


        [Fact]
        public async Task Transfer_amount_with_different_currency_should_fail()
        {
            SetupInitialData();

            var (_, isFailure, error) = await _accountPaymentService.TransferToChildAgency(1, 2, new MoneyAmount(1m, Currencies.EUR));

            Assert.True(isFailure);
        }


        [Fact]
        public async Task Transfer_when_balance_insufficent_should_fail()
        {
            SetupInitialData();

            var (_, isFailure, error) = await _accountPaymentService.TransferToChildAgency(1, 2, new MoneyAmount(1000000m, Currencies.USD));

            Assert.True(isFailure);
        }


        [Fact]
        public async Task Correct_transfer_should_succeed()
        {
            SetupInitialData();

            var (isSuccess, _, error) = await _accountPaymentService.TransferToChildAgency(1, 2, new MoneyAmount(1m, Currencies.USD));

            Assert.True(isSuccess);
        }


        [Fact]
        public async Task Correct_transfer_should_subtract_correct_value()
        {
            SetupInitialData();
            var payerAccount = _mockedEdoContext.PaymentAccounts.Single(a => a.Id == 1);

            var (isSuccess, _, error) = await _accountPaymentService.TransferToChildAgency(1, 2, new MoneyAmount(1m, Currencies.USD));

            Assert.True(isSuccess);
            Assert.Equal(999m, payerAccount.Balance);
        }


        [Fact]
        public async Task Correct_transfer_should_add_correct_value()
        {
            SetupInitialData();
            var recipientAccount = _mockedEdoContext.PaymentAccounts.Single(a => a.Id == 2);

            var (isSuccess, _, error) = await _accountPaymentService.TransferToChildAgency(1, 2, new MoneyAmount(1m, Currencies.USD));

            Assert.True(isSuccess);
            Assert.Equal(1001m, recipientAccount.Balance);
        }


        private void SetupInitialData()
        {
            SetAgencyIdForAgent(1);

            _edoContextMock
                .Setup(c => c.Agencies)
                .Returns(DbSetMockProvider.GetDbSetMock(new List<Agency>
                {
                    new Agency
                    {
                        Id = 1,
                        Name = "RootAgency",
                        ParentId = null,
                    },
                    new Agency
                    {
                        Id = 2,
                        Name = "ChildAgency",
                        ParentId = 1,
                    },
                    new Agency
                    {
                        Id = 3,
                        Name = "UnrelatedAgency",
                        ParentId = null,
                    },
                }));

            _edoContextMock
                .Setup(c => c.PaymentAccounts)
                .Returns(DbSetMockProvider.GetDbSetMock(new List<PaymentAccount>
                {
                    new PaymentAccount
                    {
                        Id = 1,
                        Balance = 1000,
                        Currency = Currencies.USD,
                        AgencyId = 1,
                        CreditLimit = 0
                    },
                    new PaymentAccount
                    {
                        Id = 2,
                        Balance = 1000,
                        Currency = Currencies.USD,
                        AgencyId = 2,
                        CreditLimit = 0
                    },
                    new PaymentAccount
                    {
                        Id = 3,
                        Balance = 1000,
                        Currency = Currencies.USD,
                        AgencyId = 3,
                        CreditLimit = 0
                    },
                    new PaymentAccount
                    {
                        Id = 4,
                        Balance = 1000,
                        Currency = Currencies.EUR,
                        AgencyId = 2,
                        CreditLimit = 0
                    },
                }));
        }


        private void SetAgencyIdForAgent(int agencyId)
        {
            var agent = new AgentContext(1, "", "", "", "", "", 1, "", agencyId, true, InAgencyPermissions.All);
            _agentContext
                .Setup(c => c.GetAgent())
                .ReturnsAsync(agent);
        }


        private Mock<EdoContext> _edoContextMock;
        private EdoContext _mockedEdoContext;
        private Mock<IAgentContextService> _agentContext;
        private IAccountPaymentService _accountPaymentService;
    }
}
