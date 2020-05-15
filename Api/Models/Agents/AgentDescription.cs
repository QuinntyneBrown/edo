using System.Collections.Generic;

namespace HappyTravel.Edo.Api.Models.Agents
{
    public readonly struct AgentDescription
    {
        public AgentDescription(string email, string lastName, string firstName, string title, string position, List<AgentAgencyInfo> counterparties)
        {
            Email = email;
            LastName = lastName;
            FirstName = firstName;
            Title = title;
            Position = position;
            Counterparties = counterparties;
        }


        /// <summary>
        ///     Agent e-mail.
        /// </summary>
        public string Email { get; }

        /// <summary>
        ///     Last name.
        /// </summary>
        public string LastName { get; }

        /// <summary>
        ///     First name.
        /// </summary>
        public string FirstName { get; }

        /// <summary>
        ///     Title (Mr., Mrs etc).
        /// </summary>
        public string Title { get; }

        /// <summary>
        ///     Agent position in counterparty.
        /// </summary>
        public string Position { get; }

        /// <summary>
        ///     List of counterparties, associated with agent.
        /// </summary>
        public List<AgentAgencyInfo> Counterparties { get; }
    }
}