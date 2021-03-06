using System.Collections.Generic;
using System.Threading.Tasks;
using CSharpFunctionalExtensions;
using HappyTravel.Edo.Api.Models.Markups;

namespace HappyTravel.Edo.Api.Services.Markups
{
    public interface IAdminMarkupPolicyManager
    {
        Task<Result> Add(MarkupPolicyData policyData);

        Task<Result> Remove(int policyId);

        Task<Result> Modify(int policyId, MarkupPolicySettings settings);

        Task<List<MarkupPolicyData>> Get(MarkupPolicyScope scope);
    }
}