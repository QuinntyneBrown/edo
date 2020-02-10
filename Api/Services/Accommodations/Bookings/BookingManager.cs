using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using CSharpFunctionalExtensions;
using HappyTravel.Edo.Api.Infrastructure;
using HappyTravel.Edo.Api.Infrastructure.DataProviders;
using HappyTravel.Edo.Api.Infrastructure.Options;
using HappyTravel.Edo.Api.Models.Accommodations;
using HappyTravel.Edo.Api.Models.Bookings;
using HappyTravel.Edo.Api.Models.Users;
using HappyTravel.Edo.Api.Services.CodeProcessors;
using HappyTravel.Edo.Api.Services.Connectors;
using HappyTravel.Edo.Api.Services.Customers;
using HappyTravel.Edo.Api.Services.Management;
using HappyTravel.Edo.Common.Enums;
using HappyTravel.Edo.Data;
using HappyTravel.EdoContracts.Accommodations;
using HappyTravel.EdoContracts.Accommodations.Enums;
using HappyTravel.EdoContracts.Accommodations.Internals;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace HappyTravel.Edo.Api.Services.Accommodations.Bookings
{
    internal class BookingManager: IBookingManager
    {
        public BookingManager(EdoContext context,
            IDateTimeProvider dateTimeProvider,
            ICustomerContext customerContext,
            IServiceAccountContext serviceAccountContext,
            ITagProcessor tagProcessor,
            IProviderRouter providerRouter,
            ILogger<BookingManager> logger)
        {
            _context = context;
            _dateTimeProvider = dateTimeProvider;
            _customerContext = customerContext;
            _serviceAccountContext = serviceAccountContext;
            _tagProcessor = tagProcessor;
            _providerRouter = providerRouter;
            _logger = logger;
        }

        
        public async Task<Result<string>> Register(AccommodationBookingRequest bookingRequest, BookingAvailabilityInfo availabilityInfo)
        {
            var (_, isCustomerFailure, customerInfo, customerError) = await _customerContext.GetCustomerInfo();

            return isCustomerFailure 
                ? ProblemDetailsBuilder.Fail<string>(customerError) 
                : Result.Ok(await CreateBooking());


            async Task<string> CreateBooking()
            {
                var tags = await GetTags();
                var initialBooking = new BookingBuilder()
                    .AddCreationDate(_dateTimeProvider.UtcNow())
                    .AddCustomerInfo(customerInfo)
                    .AddTags(tags.itn, tags.referenceCode)
                    .AddStatus(BookingStatusCodes.InternalProcessing)
                    .AddServiceDetails(availabilityInfo)
                    .AddPaymentMethod(bookingRequest.PaymentMethod)
                    .AddRequestInfo(bookingRequest)
                    .AddProviderInfo(bookingRequest.DataProvider)
                    .AddPaymentStatus(BookingPaymentStatuses.NotPaid)
                    .Build();
                
                _context.Bookings.Add(initialBooking);

                await _context.SaveChangesAsync();
                
                return tags.referenceCode;
            }
            
            
            async Task<(string itn, string referenceCode)> GetTags()
            {
                string itn;
                if (string.IsNullOrWhiteSpace(bookingRequest.ItineraryNumber))
                {
                    itn = await _tagProcessor.GenerateItn();
                }
                else
                {
                    // User can send reference code instead of itn
                    if (!_tagProcessor.TryGetItnFromReferenceCode(bookingRequest.ItineraryNumber, out itn))
                        itn = bookingRequest.ItineraryNumber;

                    if (!await AreExistBookingsForItn(itn, customerInfo.CompanyId))
                        itn = await _tagProcessor.GenerateItn();
                }

                var referenceCode = await _tagProcessor.GenerateReferenceCode(
                    ServiceTypes.HTL,
                    availabilityInfo.CountryCode,
                    itn);

                return (itn, referenceCode);
            }
        }
        
        
        public async Task<Result<BookingDetails, ProblemDetails>> Finalize(
            Data.Booking.Booking booking,
            string languageCode)
        {
            return await ExecuteBookingRequest()
                .OnSuccess(UpdateBookingData);


            Task<Result<BookingDetails, ProblemDetails>> ExecuteBookingRequest()
            {
                // TODO: will be implemented in NIJO-31 
                var bookingRequest = JsonConvert.DeserializeObject<AccommodationBookingRequest>(booking.BookingRequest);
                
                var features = new List<Feature>(); //bookingRequest.Features

                var roomDetails = bookingRequest.RoomDetails
                    .Select(d => new SlimRoomDetails(d.Type, d.Passengers, d.IsExtraBedNeeded))
                    .ToList();

                var innerRequest = new BookingRequest(bookingRequest.AvailabilityId,
                    bookingRequest.AgreementId,
                    bookingRequest.Nationality,
                    bookingRequest.PaymentMethod,
                    booking.ReferenceCode,
                    bookingRequest.Residency,
                    roomDetails,
                    features,
                    bookingRequest.RejectIfUnavailable);

                return _providerRouter.Book(booking.DataProvider, innerRequest, languageCode);
            }


            async Task<Result<BookingDetails, ProblemDetails>> UpdateBookingData(BookingDetails bookingDetails)
            {
                try
                {
                    var bookingEntity = new BookingBuilder(booking)
                        .AddBookingDetails(bookingDetails)
                        .AddStatus(BookingStatusCodes.WaitingForResponse)
                        .Build();
                    _context.Bookings.Update(bookingEntity);
                    await _context.SaveChangesAsync();
                    _context.Entry(bookingEntity).State = EntityState.Detached;
                }
                catch (Exception ex)
                {
                    var errorMessage = $"Failed to update booking data (refcode '{bookingDetails.ReferenceCode}') after the request to the connector";
                    
                    var (_, isCancellationFailed, cancellationError) = await _providerRouter.CancelBooking(booking.DataProvider, booking.ReferenceCode);
                    if (isCancellationFailed)
                        errorMessage += Environment.NewLine + $"Booking cancellation has failed: {cancellationError}";
                    
                    _logger.LogError(ex, errorMessage);

                    return ProblemDetailsBuilder.Fail<BookingDetails>(
                        $"Cannot update booking data (refcode '{bookingDetails.ReferenceCode}') after the request to the connector");
                }

                return Result.Ok<BookingDetails, ProblemDetails>(bookingDetails);
            }
        }


        public async Task<Result> UpdateBookingDetails(BookingDetails bookingDetails, Data.Booking.Booking booking)
        {
            var previousBookingDetails = JsonConvert.DeserializeObject<BookingDetails>(booking.BookingDetails);
            booking.BookingDetails = JsonConvert.SerializeObject(new BookingDetails(bookingDetails, previousBookingDetails.Agreement));
            booking.Status = bookingDetails.Status;

            _context.Bookings.Update(booking);
            await _context.SaveChangesAsync();

            return Result.Ok();
        }


        public Task<Result> ConfirmBooking(BookingDetails bookingDetails, Data.Booking.Booking booking)
        {
            booking.BookingDate = _dateTimeProvider.UtcNow();
            return UpdateBookingDetails(bookingDetails, booking);
        }


        public Task<Result> ConfirmBookingCancellation(BookingDetails bookingDetails, Data.Booking.Booking booking)
        {
            if (booking.PaymentStatus == BookingPaymentStatuses.Authorized || booking.PaymentStatus == BookingPaymentStatuses.PartiallyAuthorized)
                booking.PaymentStatus = BookingPaymentStatuses.Voided;
            if (booking.PaymentStatus == BookingPaymentStatuses.Captured)
                booking.PaymentStatus = BookingPaymentStatuses.Refunded;

            return UpdateBookingDetails(bookingDetails, booking);
        }


        public Task<Result<Data.Booking.Booking>> Get(string referenceCode)
        {
            return Get(booking => booking.ReferenceCode == referenceCode);
        }


        public Task<Result<Data.Booking.Booking>> Get(int bookingId)
        {
            return Get(booking => booking.Id == bookingId);
        }


        private async Task<Result<Data.Booking.Booking>> Get(Expression<Func<Data.Booking.Booking, bool>> filterExpression)
        {
            var booking = await _context.Bookings
                .Where(filterExpression)
                .SingleOrDefaultAsync();

            return booking == default
                ? Result.Fail<Data.Booking.Booking>("Could not get booking data")
                : Result.Ok(booking);
        }


        public async Task<Result<Data.Booking.Booking>> GetCustomersBooking(string referenceCode)
        {
            var (_, isCustomerFailure, customerData, customerError) = await _customerContext.GetCustomerInfo();
            if (isCustomerFailure)
                return Result.Fail<Data.Booking.Booking>(customerError);
            
            return await Get(booking => customerData.CustomerId == booking.CustomerId && booking.ReferenceCode == referenceCode);
        }


        public async Task<Result<AccommodationBookingInfo>> GetCustomerBookingInfo(int bookingId)
        {
            var (_, isCustomerFailure, customerData, customerError) = await _customerContext.GetCustomerInfo();
            if (isCustomerFailure)
                return Result.Fail<AccommodationBookingInfo>(customerError);

            var bookingDataResult = await Get(booking => customerData.CustomerId == booking.CustomerId && booking.Id == bookingId);
            if (bookingDataResult.IsFailure)
                return Result.Fail<AccommodationBookingInfo>(bookingDataResult.Error);

            return Result.Ok(ConvertToBookingInfo(bookingDataResult.Value));
        }


        public async Task<Result<AccommodationBookingInfo>> GetCustomerBookingInfo(string referenceCode)
        {
            var (_, isCustomerFailure, customerData, customerError) = await _customerContext.GetCustomerInfo();
            if (isCustomerFailure)
                return Result.Fail<AccommodationBookingInfo>(customerError);

            var bookingDataResult = await Get(booking => customerData.CustomerId == booking.CustomerId && booking.ReferenceCode == referenceCode);
            if (bookingDataResult.IsFailure)
                return Result.Fail<AccommodationBookingInfo>(bookingDataResult.Error);

            return Result.Ok(ConvertToBookingInfo(bookingDataResult.Value));
        }


        /// <summary>
        /// Gets all booking info of the current customer
        /// </summary>
        /// <returns>List of the slim booking models </returns>
        public async Task<Result<List<SlimAccommodationBookingInfo>>> GetCustomerBookingsInfo()
        {
            var (_, isFailure, customerData, error) = await _customerContext.GetCustomerInfo();

            if (isFailure)
                return Result.Fail<List<SlimAccommodationBookingInfo>>(error);

            var bookingData = await _context.Bookings
                .Where(b => b.CustomerId == customerData.CustomerId)
                .Select(b =>
                    new SlimAccommodationBookingInfo(b)
                ).ToListAsync();

            return Result.Ok(bookingData);
        }


        private AccommodationBookingInfo ConvertToBookingInfo(Data.Booking.Booking booking)
        {
            return new AccommodationBookingInfo(booking.Id,
                JsonConvert.DeserializeObject<AccommodationBookingDetails>(booking.BookingDetails),
                JsonConvert.DeserializeObject<BookingAvailabilityInfo>(booking.ServiceDetails),
                booking.CompanyId,
                booking.PaymentStatus);
        }
    
        
        public async Task<Result<Data.Booking.Booking, ProblemDetails>> CancelBooking(int bookingId)
        {
            var (_, isFailure, user, error) = await GetUserInfo();
            if (isFailure)
                return ProblemDetailsBuilder.Fail<Data.Booking.Booking>(error);

            var booking = await _context.Bookings
                .SingleOrDefaultAsync(b => b.Id == bookingId && (user.Type == UserTypes.ServiceAccount || b.CustomerId == user.Id));

            if (booking is null)
                return ProblemDetailsBuilder.Fail<Data.Booking.Booking>($"Could not find booking with ID '{bookingId}'");

            if (booking.Status == BookingStatusCodes.Cancelled)
                return ProblemDetailsBuilder.Fail<Data.Booking.Booking>("Booking was already cancelled");

            var (_, isCancellationFailure, _, cancellationError) = await ExecuteBookingCancellation();

            return isCancellationFailure
                ? ProblemDetailsBuilder.Fail<Data.Booking.Booking>(cancellationError.Detail)
                : Result.Ok<Data.Booking.Booking, ProblemDetails>(booking);


            Task<Result<VoidObject, ProblemDetails>> ExecuteBookingCancellation()
                => _providerRouter.CancelBooking(booking.DataProvider, booking.ReferenceCode);


            Task<Result<UserInfo>> GetUserInfo()
                => _serviceAccountContext.GetUserInfo()
                    .OnFailureCompensate(_customerContext.GetUserInfo);
        }


        // TODO: Replace method when will be added other services 
        private Task<bool> AreExistBookingsForItn(string itn, int customerId)
            => _context.Bookings.Where(b => b.CustomerId == customerId && b.ItineraryNumber == itn).AnyAsync();


        private readonly EdoContext _context;
        private readonly ICustomerContext _customerContext;
        private readonly IDataProviderClient _dataProviderClient;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly DataProviderOptions _options;
        private readonly IServiceAccountContext _serviceAccountContext;
        private readonly ITagProcessor _tagProcessor;
        private readonly IProviderRouter _providerRouter;
        private readonly ILogger<BookingManager> _logger;
    }
}