using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CSharpFunctionalExtensions;
using HappyTravel.Edo.Api.Infrastructure;
using HappyTravel.Edo.Api.Models.Agents;
using HappyTravel.Edo.Api.Models.Management.Enums;
using HappyTravel.Edo.Api.Models.Markups;
using HappyTravel.Edo.Api.Services.Agents;
using HappyTravel.Edo.Api.Services.Management;
using HappyTravel.Edo.Api.Services.Markups.Templates;
using HappyTravel.Edo.Common.Enums.Markup;
using HappyTravel.Edo.Data;
using HappyTravel.Edo.Data.Markup;
using Microsoft.EntityFrameworkCore;

namespace HappyTravel.Edo.Api.Services.Markups
{
    public class AgentMarkupPolicyManager : IAgentMarkupPolicyManager
    {
        public AgentMarkupPolicyManager(EdoContext context,
            IAdministratorContext administratorContext,
            IMarkupPolicyTemplateService templateService,
            IDateTimeProvider dateTimeProvider)
        {
            _context = context;
            _administratorContext = administratorContext;
            _templateService = templateService;
            _dateTimeProvider = dateTimeProvider;
        }


        public Task<Result> Add(MarkupPolicyData policyData, AgentContext agent)
        {
            return ValidatePolicy(policyData)
                .Bind(CheckPermissions)
                .Tap(SavePolicy);

            Task<Result> CheckPermissions() => CheckUserManagePermissions(policyData.Scope, agent);


            async Task SavePolicy()
            {
                var now = _dateTimeProvider.UtcNow();
                var (type, counterpartyId, agencyId, agentId) = policyData.Scope;

                var policy = new MarkupPolicy
                {
                    Description = policyData.Settings.Description,
                    Order = policyData.Settings.Order,
                    ScopeType = type,
                    Target = policyData.Target,
                    AgencyId = agencyId,
                    CounterpartyId = counterpartyId,
                    AgentId = agentId,
                    TemplateSettings = policyData.Settings.TemplateSettings,
                    Currency = policyData.Settings.Currency,
                    Created = now,
                    Modified = now,
                    TemplateId = policyData.Settings.TemplateId
                };

                _context.MarkupPolicies.Add(policy);
                await _context.SaveChangesAsync();
            }
        }


        public Task<Result> Remove(int policyId, AgentContext agent)
        {
            return GetPolicy()
                .Bind(CheckPermissions)
                .Bind(DeletePolicy);


            async Task<Result<MarkupPolicy>> GetPolicy()
            {
                var policy = await _context.MarkupPolicies.SingleOrDefaultAsync(p => p.Id == policyId);
                return policy == null
                    ? Result.Failure<MarkupPolicy>("Could not find policy")
                    : Result.Success(policy);
            }


            async Task<Result<MarkupPolicy>> CheckPermissions(MarkupPolicy policy)
            {
                var scopeType = policy.ScopeType;
                var scope = new MarkupPolicyScope(scopeType,
                    policy.CounterpartyId ?? policy.AgencyId ?? policy.AgentId);

                var (_, isFailure, error) = await CheckUserManagePermissions(scope, agent);
                return isFailure
                    ? Result.Failure<MarkupPolicy>(error)
                    : Result.Success(policy);
            }


            async Task<Result> DeletePolicy(MarkupPolicy policy)
            {
                _context.Remove(policy);
                await _context.SaveChangesAsync();
                return Result.Success();
            }
        }


        public async Task<Result> Modify(int policyId, MarkupPolicySettings settings, AgentContext agent)
        {
            var policy = await _context.MarkupPolicies.SingleOrDefaultAsync(p => p.Id == policyId);
            if (policy == null)
                return Result.Failure("Could not find policy");

            return await Result.Success()
                .Bind(CheckPermissions)
                .Bind(UpdatePolicy);


            Task<Result> CheckPermissions()
            {
                var scopeData = new MarkupPolicyScope(policy.ScopeType,
                    policy.CounterpartyId ?? policy.AgencyId ?? policy.AgentId);

                return CheckUserManagePermissions(scopeData, agent);
            }


            async Task<Result> UpdatePolicy()
            {
                policy.Description = settings.Description;
                policy.Order = settings.Order;
                policy.TemplateId = settings.TemplateId;
                policy.TemplateSettings = settings.TemplateSettings;
                policy.Currency = settings.Currency;
                policy.Modified = _dateTimeProvider.UtcNow();

                var validateResult = await ValidatePolicy(GetPolicyData(policy));
                if (validateResult.IsFailure)
                    return validateResult;

                _context.Update(policy);
                await _context.SaveChangesAsync();
                return Result.Success();
            }
        }


        public async Task<List<MarkupPolicyData>> Get(MarkupPolicyScope scope)
        {
            return (await GetPoliciesForScope(scope))
                .Select(GetPolicyData)
                .ToList();
        }


        private Task<List<MarkupPolicy>> GetPoliciesForScope(MarkupPolicyScope scope)
        {
            var (type, counterpartyId, agencyId, agentId) = scope;
            return type switch
            {
                MarkupPolicyScopeType.Global => _context.MarkupPolicies.Where(p => p.ScopeType == MarkupPolicyScopeType.Global).ToListAsync(),
                MarkupPolicyScopeType.Counterparty => _context.MarkupPolicies
                    .Where(p => p.ScopeType == MarkupPolicyScopeType.Counterparty && p.CounterpartyId == counterpartyId)
                    .ToListAsync(),
                MarkupPolicyScopeType.Agency => _context.MarkupPolicies.Where(p => p.ScopeType == MarkupPolicyScopeType.Counterparty && p.AgencyId == agencyId)
                    .ToListAsync(),
                MarkupPolicyScopeType.Agent => _context.MarkupPolicies.Where(p => p.ScopeType == MarkupPolicyScopeType.Counterparty && p.AgentId == agentId)
                    .ToListAsync(),
                _ => Task.FromResult(new List<MarkupPolicy>(0))
            };
        }


        private async Task<Result> CheckUserManagePermissions(MarkupPolicyScope scope, AgentContext agent)
        {
            var hasAdminPermissions = await _administratorContext.HasPermission(AdministratorPermissions.MarkupManagement);
            if (hasAdminPermissions)
                return Result.Success();

            var (type, counterpartyId, agencyId, agentId) = scope;
            switch (type)
            {
                case MarkupPolicyScopeType.Agent:
                {
                    var isMasterAgentInUserCounterparty = agent.CounterpartyId == counterpartyId
                        && agent.IsMaster;

                    return isMasterAgentInUserCounterparty
                        ? Result.Success()
                        : Result.Failure("Permission denied");
                }
                case MarkupPolicyScopeType.Agency:
                {
                    var agency = await _context.Agencies
                        .SingleOrDefaultAsync(a => a.Id == agencyId);

                    if (agency == null)
                        return Result.Failure("Could not find agency");

                    var isMasterAgent = agent.CounterpartyId == agency.CounterpartyId
                        && agent.IsMaster;

                    return isMasterAgent
                        ? Result.Success()
                        : Result.Failure("Permission denied");
                }
                case MarkupPolicyScopeType.EndClient:
                {
                    return agent.AgentId == agentId
                        ? Result.Success()
                        : Result.Failure("Permission denied");
                }
                default:
                    return Result.Failure("Permission denied");
            }
        }


        private static MarkupPolicyData GetPolicyData(MarkupPolicy policy)
        {
            return new MarkupPolicyData(policy.Target,
                new MarkupPolicySettings(policy.Description, policy.TemplateId, policy.TemplateSettings, policy.Order, policy.Currency),
                GetPolicyScope());


            MarkupPolicyScope GetPolicyScope()
            {
                // Policy can belong to counterparty, agency or agent.
                var scopeId = policy.CounterpartyId ?? policy.AgencyId ?? policy.AgentId;
                return new MarkupPolicyScope(policy.ScopeType, scopeId);
            }
        }


        private Task<Result> ValidatePolicy(MarkupPolicyData policyData)
        {
            return ValidateTemplate()
                .Ensure(ScopeIsValid, "Invalid scope data")
                .Ensure(TargetIsValid, "Invalid policy target")
                .Ensure(PolicyOrderIsUniqueForScope, "Policy with same order is already defined");


            Result ValidateTemplate() => _templateService.Validate(policyData.Settings.TemplateId, policyData.Settings.TemplateSettings);


            bool ScopeIsValid()
            {
                var (type, counterpartyId, _, _) = policyData.Scope;
                return type switch
                {
                    MarkupPolicyScopeType.Global => counterpartyId == null,
                    MarkupPolicyScopeType.Counterparty => counterpartyId != null,
                    MarkupPolicyScopeType.Agency => counterpartyId != null,
                    MarkupPolicyScopeType.Agent => counterpartyId != null,
                    MarkupPolicyScopeType.EndClient => counterpartyId != null,
                    _ => false
                };
            }


            bool TargetIsValid() => policyData.Target != MarkupPolicyTarget.NotSpecified;


            async Task<bool> PolicyOrderIsUniqueForScope()
            {
                var isSameOrderPolicyExist = (await GetPoliciesForScope(policyData.Scope))
                    .Any(p => p.Order == policyData.Settings.Order);

                return !isSameOrderPolicyExist;
            }
        }


        private readonly IAdministratorContext _administratorContext;
        private readonly IMarkupPolicyTemplateService _templateService;
        private readonly EdoContext _context;
        private readonly IDateTimeProvider _dateTimeProvider;
    }
}