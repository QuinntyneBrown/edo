using System.Collections.Generic;
using CSharpFunctionalExtensions;
using HappyTravel.Edo.Api.Extensions;
using HappyTravel.Edo.Common.Enums;

namespace HappyTravel.Edo.Api.Models.Agents
{
    public readonly struct AgentInfoInAgency
    {
        public AgentInfoInAgency(int agentId, string firstName, string lastName, string email,
            string title, string position, int counterpartyId, string counterpartyName, int agencyId, string agencyName,
            bool isMaster, List<InCounterpartyPermissions> inCounterpartyPermissions)
        {
            AgentId = agentId;
            FirstName = firstName;
            LastName = lastName;
            Email = email;
            Title = title;
            Position = position;
            CounterpartyId = counterpartyId;
            CounterpartyName = counterpartyName;
            AgencyId = agencyId;
            AgencyName = agencyName;
            IsMaster = isMaster;
            InCounterpartyPermissions = inCounterpartyPermissions;
        }


        /// <summary>
        ///     Agent ID.
        /// </summary>
        public int AgentId { get; }

        /// <summary>
        ///     First name.
        /// </summary>
        public string FirstName { get; }

        /// <summary>
        ///     Last name.
        /// </summary>
        public string LastName { get; }

        /// <summary>
        ///     Agent e-mail.
        /// </summary>
        public string Email { get; }

        /// <summary>
        ///     ID of the agent's counterparty.
        /// </summary>
        public int CounterpartyId { get; }

        /// <summary>
        ///     Name of the agent's counterparty.
        /// </summary>
        public string CounterpartyName { get; }

        /// <summary>
        ///     ID of the agent's agency.
        /// </summary>
        public int AgencyId { get; }

        /// <summary>
        ///     Name of the agent's agency.
        /// </summary>
        public string AgencyName { get; }

        /// <summary>
        ///     Indicates whether the agent is master or regular agent.
        /// </summary>
        public bool IsMaster { get; }

        /// <summary>
        ///     Title (Mr., Mrs etc).
        /// </summary>
        public string Title { get; }

        /// <summary>
        ///     Agent position in counterparty.
        /// </summary>
        public string Position { get; }

        /// <summary>
        ///     Permissions of the agent.
        /// </summary>
        public List<InCounterpartyPermissions> InCounterpartyPermissions { get; }
    }
}