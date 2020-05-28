using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CSharpFunctionalExtensions;
using HappyTravel.Edo.Api.Extensions;
using HappyTravel.Edo.Api.Models.Agents;
using HappyTravel.Edo.Common.Enums;
using HappyTravel.Edo.Data;
using HappyTravel.Edo.Data.Agents;
using Microsoft.EntityFrameworkCore;

namespace HappyTravel.Edo.Api.Services.Agents
{
    public class AgentPermissionManagementService : IAgentPermissionManagementService
    {
        public AgentPermissionManagementService(EdoContext context,
            IAgentContext agentContext, IPermissionChecker permissionChecker)
        {
            _context = context;
            _agentContext = agentContext;
            _permissionChecker = permissionChecker;
        }


        public Task<Result<List<InAgencyPermissions>>> SetInAgencyPermissions(int agencyId, int agentId,
            List<InAgencyPermissions> permissionsList) =>
            SetInAgencyPermissions(agencyId, agentId, permissionsList.Aggregate((p1, p2) => p1 | p2));


        public async Task<Result<List<InAgencyPermissions>>> SetInAgencyPermissions(int agencyId, int agentId,
            InAgencyPermissions permissions)
        {
            var agent = await _agentContext.GetAgent();

            return await CheckPermission()
                .Bind(CheckCounterpartyAndAgency)
                .Bind(GetRelation)
                .Ensure(IsPermissionManagementRightNotLost, "Cannot revoke last permission management rights")
                .Map(UpdatePermissions);

            Result CheckPermission()
            {
                if (!agent.InAgencyPermissions.HasFlag(InAgencyPermissions.PermissionManagementInAgency)
                    && !agent.InAgencyPermissions.HasFlag(InAgencyPermissions.PermissionManagementInCounterparty))
                    return Result.Failure("You have no acceptance to manage agents permissions");

                return Result.Ok();
            }

            Result CheckCounterpartyAndAgency()
            {
                if (!agent.InAgencyPermissions.HasFlag(InAgencyPermissions.PermissionManagementInCounterparty)
                    && agent.AgencyId != agencyId)
                {
                    return Result.Failure("The agent isn't affiliated with the agency");
                }
                
                return Result.Ok();
            }

            async Task<Result<AgentAgencyRelation>> GetRelation()
            {
                var relation = await _context.AgentAgencyRelations
                    .SingleOrDefaultAsync(r => r.AgentId == agentId && r.AgencyId == agencyId);

                return relation is null
                    ? Result.Failure<AgentAgencyRelation>(
                        $"Could not find relation between the agent {agentId} and the agency {agencyId}")
                    : Result.Ok(relation);
            }


            async Task<bool> IsPermissionManagementRightNotLost(AgentAgencyRelation relation)
            {
                if (permissions.HasFlag(InAgencyPermissions.PermissionManagementInCounterparty))
                    return true;

                return (await _context.AgentAgencyRelations
                        .Where(r => r.AgencyId == relation.AgencyId && r.AgentId != relation.AgentId)
                        .ToListAsync())
                    .Any(c => c.InAgencyPermissions.HasFlag(InAgencyPermissions.PermissionManagementInCounterparty));
            }


            async Task<List<InAgencyPermissions>> UpdatePermissions(AgentAgencyRelation relation)
            {
                relation.InAgencyPermissions = permissions;

                _context.AgentAgencyRelations.Update(relation);
                await _context.SaveChangesAsync();

                return relation.InAgencyPermissions.ToList();
            }
        }


        private readonly EdoContext _context;
        private readonly IAgentContext _agentContext;
        private readonly IPermissionChecker _permissionChecker;
    }
}