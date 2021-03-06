using System.Threading.Tasks;
using CSharpFunctionalExtensions;
using HappyTravel.Edo.Api.Models.Agents;

namespace HappyTravel.Edo.Api.Services.Agents
{
    public interface IAgentRegistrationService
    {
        Task<Result> RegisterWithCounterparty(AgentEditableInfo agentData, CounterpartyEditRequest counterpartyData,
            string externalIdentity, string email);


        Task<Result> RegisterInvited(AgentEditableInfo registrationInfo,
            string invitationCode, string externalIdentity, string email);
    }
}