using System.Threading.Tasks;
using CSharpFunctionalExtensions;
using HappyTravel.Edo.Api.Infrastructure;
using HappyTravel.Edo.Api.Infrastructure.FunctionalExtensions;
using HappyTravel.Edo.Api.Infrastructure.Logging;
using HappyTravel.Edo.Api.Models.Customers;
using HappyTravel.Edo.Common.Enums;
using HappyTravel.Edo.Data;
using HappyTravel.Edo.Data.Customers;
using HappyTravel.MailSender;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HappyTravel.Edo.Api.Services.Customers
{
    public class CustomerRegistrationService : ICustomerRegistrationService
    {
        public CustomerRegistrationService(EdoContext context,
            ICompanyService companyService,
            ICustomerService customerService,
            ICustomerInvitationService customerInvitationService,
            IOptions<CustomerRegistrationNotificationOptions> notificationOptions,
            IMailSender mailSender,
            ILogger<CustomerRegistrationService> logger)
        {
            _context = context;
            _companyService = companyService;
            _customerService = customerService;
            _customerInvitationService = customerInvitationService;
            _notificationOptions = notificationOptions.Value;
            _mailSender = mailSender;
            _logger = logger;
        }


        public Task<Result> RegisterWithCompany(CustomerRegistrationInfo customerData,
            CompanyRegistrationInfo companyData,
            string externalIdentity,
            string email)
        {
            return Result.Ok()
                .Ensure(IdentityIsPresent, "User should have identity")
                .OnSuccessWithTransaction(_context, () => Result.Ok()
                    .OnSuccess(CreateCompany)
                    .OnSuccess(CreateCustomer)
                    .OnSuccess(AddMasterCompanyRelation))
                .OnSuccess(LogSuccess)
                .OnSuccess(SendRegistrationMailToAdmins)
                .OnFailure(LogFailure);


            bool IdentityIsPresent()
            {
                return !string.IsNullOrWhiteSpace(externalIdentity);
            }


            Task<Result<Company>> CreateCompany()
            {
                return _companyService.Add(companyData);
            }


            async Task<Result<(Company, Customer)>> CreateCustomer(Company company)
            {
                var (_, isFailure, customer, error) = await _customerService.Add(customerData, externalIdentity, email);
                return isFailure
                    ? Result.Fail<(Company, Customer)>(error)
                    : Result.Ok((company1: company, customer));
            }


            Task AddMasterCompanyRelation((Company company, Customer customer) companyUserInfo)
            {
                return AddCompanyRelation(companyUserInfo.customer,
                    companyUserInfo.company.Id,
                    CustomerCompanyRelationTypes.Master,
                    InCompanyPermissions.All);
            }


            async Task<Result> SendRegistrationMailToAdmins()
            {
                var customer = $"{customerData.Title} {customerData.FirstName} {customerData.LastName}";
                if (!string.IsNullOrWhiteSpace(customerData.Position))
                    customer += $" ({customerData.Position})";
                
                var messageData = new
                {
                    company = companyData,
                    customerName = customer
                };

                return await _mailSender.Send(_notificationOptions.MasterCustomerMailTemplateId, _notificationOptions.AdministratorsEmails, messageData);
            }


            Result LogSuccess((Company, Customer) registrationData)
            {
                var (company, customer) = registrationData;
                _logger.LogCustomerRegistrationSuccess($"Customer {customer.Email} with company {company.Name} successfully registered");
                return Result.Ok();
            }


            void LogFailure(string error)
            {
                _logger.LogCustomerRegistrationFailed(error);
            }
        }


        public Task<Result> RegisterInvited(CustomerRegistrationInfo registrationInfo,
            string invitationCode, string externalIdentity, string email)
        {
            return Result.Ok()
                .Ensure(IdentityIsPresent, "User should have identity")
                .OnSuccess(GetPendingInvitation)
                .OnSuccessWithTransaction(_context, invitation => Result.Ok(invitation)
                    .OnSuccess(CreateCustomer)
                    .OnSuccess(AddRegularCompanyRelation)
                    .OnSuccess(AcceptInvitation))
                .OnSuccess(LogSuccess)
                .OnSuccess(GetMasterCustomer)
                .OnSuccess(SendRegistrationMailToMaster)
                .OnFailure(LogFailed);


            bool IdentityIsPresent()
            {
                return !string.IsNullOrWhiteSpace(externalIdentity);
            }


            Task<Result<CustomerInvitationInfo>> GetPendingInvitation()
            {
                return _customerInvitationService.GetPendingInvitation(invitationCode);
            }


            Task<Result<Customer>> GetMasterCustomer(CustomerInvitationInfo invitationInfo) => _customerService.GetMasterCustomer(invitationInfo.CompanyId);


            async Task<Result<(CustomerInvitationInfo, Customer)>> CreateCustomer(CustomerInvitationInfo invitation)
            {
                var (_, isFailure, customer, error) = await _customerService.Add(registrationInfo, externalIdentity, email);
                return isFailure
                    ? Result.Fail<(CustomerInvitationInfo, Customer)>(error)
                    : Result.Ok((invitation, customer));
            }


            Task AddRegularCompanyRelation((CustomerInvitationInfo invitation, Customer customer) invitationData)
            {
                return AddCompanyRelation(invitationData.customer,
                    invitationData.invitation.CompanyId,
                    CustomerCompanyRelationTypes.Regular,
                    DefaultCustomerPermissions);
            }


            async Task<CustomerInvitationInfo> AcceptInvitation(
                (CustomerInvitationInfo invitationInfo, Customer customer) invitationData)
            {
                await _customerInvitationService.AcceptInvitation(invitationCode);
                return invitationData.invitationInfo;
            }


            async Task<Result> SendRegistrationMailToMaster(Customer master)
            {
                var position = registrationInfo.Position;
                if (string.IsNullOrWhiteSpace(position))
                    position = "a new employee";

                var (_, isFailure, error) = await _mailSender.Send(_notificationOptions.RegularCustomerMailTemplateId, master.Email, new
                {
                    customerName = $"{registrationInfo.FirstName} {registrationInfo.LastName}",
                    position = position,
                    title = registrationInfo.Title
                });
                if (isFailure)
                    return Result.Fail(error);

                return Result.Ok();
            }


            Result<CustomerInvitationInfo> LogSuccess(CustomerInvitationInfo invitationInfo)
            {
                _logger.LogCustomerRegistrationSuccess($"Customer {email} successfully registered and bound to company ID:'{invitationInfo.CompanyId}'");
                return Result.Ok(invitationInfo);
            }


            void LogFailed(string error)
            {
                _logger.LogCustomerRegistrationFailed(error);
            }
        }


        private Task AddCompanyRelation(Customer customer, int companyId, CustomerCompanyRelationTypes relationType, InCompanyPermissions permissions)
        {
            _context.CustomerCompanyRelations.Add(new CustomerCompanyRelation
            {
                CompanyId = companyId,
                CustomerId = customer.Id,
                Type = relationType,
                InCompanyPermissions = permissions
            });

            return _context.SaveChangesAsync();
        }


        private const InCompanyPermissions DefaultCustomerPermissions = InCompanyPermissions.AccommodationAvailabilitySearch |
            InCompanyPermissions.AccommodationBooking;

        private readonly EdoContext _context;
        private readonly ICompanyService _companyService;
        private readonly ICustomerService _customerService;
        private readonly ICustomerInvitationService _customerInvitationService;
        private readonly CustomerRegistrationNotificationOptions _notificationOptions;
        private readonly IMailSender _mailSender;
        private readonly ILogger<CustomerRegistrationService> _logger;
    }
}