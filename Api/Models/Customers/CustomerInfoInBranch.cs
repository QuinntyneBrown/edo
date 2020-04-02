using System.Collections.Generic;
using CSharpFunctionalExtensions;
using HappyTravel.Edo.Api.Extensions;
using HappyTravel.Edo.Common.Enums;

namespace HappyTravel.Edo.Api.Models.Customers
{
    public readonly struct CustomerInfoInBranch
    {
        public CustomerInfoInBranch(int customerId, string firstName, string lastName, string email,
            string title, string position, int companyId, string companyName, int branchId, string branchName,
            bool isMaster, List<InCompanyPermissions> inCompanyPermissions)
        {
            CustomerId = customerId;
            FirstName = firstName;
            LastName = lastName;
            Email = email;
            Title = title;
            Position = position;
            CompanyId = companyId;
            CompanyName = companyName;
            BranchId = branchId;
            BranchName = branchName;
            IsMaster = isMaster;
            InCompanyPermissions = inCompanyPermissions;
        }


        /// <summary>
        ///     Customer ID.
        /// </summary>
        public int CustomerId { get; }

        /// <summary>
        ///     First name.
        /// </summary>
        public string FirstName { get; }

        /// <summary>
        ///     Last name.
        /// </summary>
        public string LastName { get; }

        /// <summary>
        ///     Customer e-mail.
        /// </summary>
        public string Email { get; }

        /// <summary>
        ///     ID of the customer's company.
        /// </summary>
        public int CompanyId { get; }

        /// <summary>
        ///     Name of the customer's company.
        /// </summary>
        public string CompanyName { get; }

        /// <summary>
        ///     ID of the customer's branch.
        /// </summary>
        public int BranchId { get; }

        /// <summary>
        ///     Name of the customer's branch.
        /// </summary>
        public string BranchName { get; }

        /// <summary>
        ///     Indicates whether the customer is master or regular customer.
        /// </summary>
        public bool IsMaster { get; }

        /// <summary>
        ///     Title (Mr., Mrs etc).
        /// </summary>
        public string Title { get; }

        /// <summary>
        ///     Customer position in company.
        /// </summary>
        public string Position { get; }

        /// <summary>
        ///     Permissions of the customer.
        /// </summary>
        public List<InCompanyPermissions> InCompanyPermissions { get; }
    }
}