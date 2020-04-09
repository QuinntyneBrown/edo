using System.Threading.Tasks;
using HappyTravel.Edo.Api.Infrastructure.Options;
using HappyTravel.Edo.Api.Models.Agents;
using HappyTravel.Edo.Api.Services.Agents;
using HappyTravel.Edo.Api.Services.Users;
using HappyTravel.Edo.UnitTests.Infrastructure;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace HappyTravel.Edo.UnitTests.Agents.Invitations
{
    public class InvitationsToOtherCounterparty
    {
        public InvitationsToOtherCounterparty()
        {
            var agent = AgentInfoFactory.CreateByWithCounterpartyAndAgency(It.IsAny<int>(), AgentCounterpartyId, It.IsAny<int>());
            var agentContext = new Mock<IAgentContext>();
            agentContext
                .Setup(c => c.GetAgent())
                .ReturnsAsync(agent);
            
            _invitationService = new AgentInvitationService(agentContext.Object,
                Mock.Of<IOptions<AgentInvitationOptions>>(),
                Mock.Of<IUserInvitationService>(),
                Mock.Of<ICounterpartyService>());
        }
        
        [Fact]
        public async Task Sending_invitation_to_other_counterparty_should_be_permitted()
        {
            var invitationInfoWithOtherCounterparty = new AgentInvitationInfo(It.IsAny<AgentEditableInfo>(),
                OtherCounterpartyId, It.IsAny<string>());
            
            var (_, isFailure, _) = await _invitationService.Send(invitationInfoWithOtherCounterparty);
            
            Assert.True(isFailure);
        }
        
        [Fact]
        public async Task Creating_invitation_to_other_counterparty_should_be_permitted()
        {
            var invitationInfoWithOtherCounterparty = new AgentInvitationInfo(It.IsAny<AgentEditableInfo>(),
                OtherCounterpartyId, It.IsAny<string>());
            
            var (_, isFailure, _, _) = await _invitationService.Create(invitationInfoWithOtherCounterparty);
            
            Assert.True(isFailure);
        }
        
        private readonly AgentInvitationService _invitationService;
        private const int AgentCounterpartyId = 123;
        private const int OtherCounterpartyId = 122;
    }
}