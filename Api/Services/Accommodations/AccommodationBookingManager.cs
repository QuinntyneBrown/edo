using System;
using System.Linq;
using System.Threading.Tasks;
using CSharpFunctionalExtensions;
using HappyTravel.Edo.Api.Infrastructure;
using HappyTravel.Edo.Api.Models.Bookings;
using HappyTravel.Edo.Api.Services.CodeGeneration;
using HappyTravel.Edo.Api.Services.Customers;
using HappyTravel.Edo.Common.Enums;
using HappyTravel.Edo.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HappyTravel.Edo.Api.Services.Accommodations
{
    internal class AccommodationBookingManager : IAccommodationBookingManager
    {
        public AccommodationBookingManager(IOptions<DataProviderOptions> options,
            IDataProviderClient dataProviderClient, 
            EdoContext context,
            IDateTimeProvider dateTimeProvider,
            ICustomerContext customerContext,
            ITagGenerator tagGenerator)
        {
            _dataProviderClient = dataProviderClient;
            _options = options.Value;
            _context = context;
            _dateTimeProvider = dateTimeProvider;
            _customerContext = customerContext;
            _tagGenerator = tagGenerator;
        }

        public async Task<Result<AccommodationBookingDetails, ProblemDetails>> Book(AccommodationBookingRequest bookingRequest, 
            BookingAvailabilityInfo availability,
            string languageCode)
        {
            var (_, isCustomerFailure, customer, customerError) = await _customerContext.GetCustomer();
            if (isCustomerFailure)
                return ProblemDetailsBuilder.Fail<AccommodationBookingDetails>(customerError);

            var (_, isCompanyFailure, company, companyError) = await _customerContext.GetCompany();
            if(isCompanyFailure)
                return ProblemDetailsBuilder.Fail<AccommodationBookingDetails>(companyError);

            var itn = !string.IsNullOrWhiteSpace(bookingRequest.ItineraryNumber) 
                ? bookingRequest.ItineraryNumber 
                : await _tagGenerator.GenerateItn();
            
            var referenceCode = await _tagGenerator.GenerateReferenceCode(ServiceTypes.HTL,
                availability.CountryCode,
                itn);
            
            return await ExecuteBookingRequest()
                .OnSuccess(async confirmedBooking => await SaveBookingResult(confirmedBooking));
            
            Task<Result<AccommodationBookingDetails, ProblemDetails>> ExecuteBookingRequest()
            {
                var innerRequest = new InnerAccommodationBookingRequest(bookingRequest,
                    availability, referenceCode);
                
                return _dataProviderClient.Post<InnerAccommodationBookingRequest, AccommodationBookingDetails>(
                    new Uri(_options.Netstorming + "hotels/booking", UriKind.Absolute),
                    innerRequest, languageCode);
            }

            Task SaveBookingResult(AccommodationBookingDetails confirmedBooking)
            {
                var booking = new AccommodationBookingBuilder()
                    .AddCustomerInfo(customer, company)
                    .AddTags(itn, referenceCode)
                    .AddRequestInfo(bookingRequest)
                    .AddConfirmationDetails(confirmedBooking)
                    .AddServiceDetails(availability)
                    .AddCreationDate(_dateTimeProvider.UtcNow())
                    .Build();

                _context.Bookings.Add(booking);
                return _context.SaveChangesAsync();
            }
        }

        public async Task<AccommodationBookingInfo[]> GetBookings()
        {
            var (_, isFailure, customer, _) = await _customerContext.GetCustomer();
            if (isFailure)
                return Array.Empty<AccommodationBookingInfo>();

            return await _context.Bookings
                .Where(b => b.CustomerId == customer.Id)
                .Select(b => new AccommodationBookingInfo(b.Id, b.BookingDetails, b.ServiceDetails, b.CompanyId))
                .ToArrayAsync();
        }

        private readonly EdoContext _context;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly ICustomerContext _customerContext;
        private readonly ITagGenerator _tagGenerator;
        private readonly IDataProviderClient _dataProviderClient;
        private readonly DataProviderOptions _options;
    }
}