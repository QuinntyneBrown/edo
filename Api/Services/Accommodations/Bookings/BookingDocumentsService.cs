using System.Collections.Generic;
using System.Threading.Tasks;
using CSharpFunctionalExtensions;
using HappyTravel.Edo.Api.Infrastructure.Options;
using HappyTravel.Edo.Api.Models.Bookings;
using HappyTravel.Edo.Api.Models.Mailing;
using HappyTravel.Edo.Api.Services.Agents;
using HappyTravel.EdoContracts.Accommodations;
using HappyTravel.EdoContracts.Accommodations.Internals;
using HappyTravel.Money.Enums;
using Microsoft.Extensions.Options;

namespace HappyTravel.Edo.Api.Services.Accommodations.Bookings
{
    public class BookingDocumentsService : IBookingDocumentsService
    {
        public BookingDocumentsService(IOptions<BankDetails> bankDetails, 
            IBookingRecordsManager bookingRecordsManager, 
            IAccommodationService accommodationService,
            ICounterpartyService counterpartyService)
        {
            _bankDetails = bankDetails.Value;
            _bookingRecordsManager = bookingRecordsManager;
            _accommodationService = accommodationService;
            _counterpartyService = counterpartyService;
        }


        public Task<Result<BookingVoucherData>> GenerateVoucher(int bookingId, string languageCode)
        {
            return GetBookingData(bookingId)
                .OnSuccess(CreateVoucherData);


            async Task<Result<BookingVoucherData>> CreateVoucherData(
                (AccommodationBookingInfo bookingInfo, AccommodationBookingDetails bookingDetails) bookingData)
            {
                var bookingDetails = bookingData.bookingDetails;

                var (_, isBookingFailure, booking, bookingError) = await _bookingRecordsManager.Get(bookingId);
                if (isBookingFailure)
                    return Result.Fail<BookingVoucherData>(bookingError);

                var (_, isAccommodationFailure, accommodationDetails, accommodationError) = await _accommodationService.Get(booking.DataProvider, bookingDetails.AccommodationId, languageCode);
                if (isAccommodationFailure)
                    return Result.Fail<BookingVoucherData>(accommodationError.Detail);

                return Result.Ok(new BookingVoucherData
                (
                    booking.Id,
                    GetAccommodationInfo(in accommodationDetails),
                    bookingDetails.CheckInDate,
                    bookingDetails.CheckOutDate,
                    bookingDetails.DeadlineDate,
                    booking.MainPassengerName,
                    booking.ReferenceCode,
                    booking.Rooms,
                    accommodationDetails.Name
                ));
            }
        }
        
        private static BookingVoucherData.AccommodationInfo GetAccommodationInfo(in AccommodationDetails details)
        {
            var location = new SlimLocationInfo(details.Location.Address, details.Location.Country, details.Location.Locality, details.Location.LocalityZone, details.Location.Coordinates);
            return new BookingVoucherData.AccommodationInfo(details.Name, in location, details.Contacts);
        }


        public Task<Result<BookingInvoiceData>> GenerateInvoice(int bookingId, string languageCode)
        {
            return GetBookingData(bookingId)
                .OnSuccess(CreateInvoiceData);


            async Task<Result<BookingInvoiceData>> CreateInvoiceData(
                (AccommodationBookingInfo bookingInfo, AccommodationBookingDetails bookingDetails) bookingData)
            {
                var bookingDetails = bookingData.bookingDetails;

                var (_, isBookingFailure, booking, bookingError) = await _bookingRecordsManager.Get(bookingId);
                if (isBookingFailure)
                    return Result.Fail<BookingInvoiceData>(bookingError);

                var (_, isAccommodationFailure, accommodationDetails, accommodationError) =
                    await _accommodationService.Get(booking.DataProvider, bookingDetails.AccommodationId, languageCode);
                if (isAccommodationFailure)
                    return Result.Fail<BookingInvoiceData>(accommodationError.Detail);

                var (_, isCompanyFailure, company, companyError) = await _counterpartyService.Get(booking.CounterpartyId);
                if (isCompanyFailure)
                    return Result.Fail<BookingInvoiceData>(companyError);

                if (!_bankDetails.AccountDetails.TryGetValue(bookingData.bookingInfo.TotalPrice.Currency, out var accountData))
                    _bankDetails.AccountDetails.TryGetValue(Currencies.USD, out accountData);
                
                var sellerDetails = new BookingInvoiceData.SellerInfo(_bankDetails.CompanyName, _bankDetails.BankName, _bankDetails.BankAddress,
                    accountData.AccountNumber, accountData.Iban, _bankDetails.RoutingCode, _bankDetails.SwiftCode);

                //TODO: add a contract number and a billing email after company table refactoring
                var buyerDetails = new BookingInvoiceData.BuyerInfo(company.Name, company.Address, "contractNumber", "billingEmail");

                var roomDetails = new List<BookingRoomDetails>();


                /*CheckInDate = bookingDetails.CheckInDate.ToString("d"),
                CheckOutDate = bookingDetails.CheckOutDate.ToString("d"),
                RoomDetails = bookingDetails.RoomDetails,
                CurrencyCode = Currencies.ToCurrencyCode(serviceDetails.Agreement.Price.Currency),
                PriceTotal = serviceDetails.Agreement.Price.NetTotal.ToString(CultureInfo.InvariantCulture),
                AccommodationName = serviceDetails.AccommodationName*/
                return Result.Ok(new BookingInvoiceData(booking.Id, in buyerDetails, in sellerDetails, booking.ReferenceCode, roomDetails, booking.Created,
                    bookingDetails.DeadlineDate ?? bookingDetails.CheckInDate));
            }
        }


        private async Task<Result<(AccommodationBookingInfo, AccommodationBookingDetails)>> GetBookingData(int bookingId)
        {
            var (_, isFailure, bookingInfo, error) = await _bookingRecordsManager.GetAgentBookingInfo(bookingId);

            if (isFailure)
                return Result.Fail<(AccommodationBookingInfo, AccommodationBookingDetails)>(error);

            return Result.Ok((bookingInfo, bookingInfo.BookingDetails));
        }


        private readonly BankDetails _bankDetails;
        private readonly IBookingRecordsManager _bookingRecordsManager;
        private readonly IAccommodationService _accommodationService;
        private readonly ICounterpartyService _counterpartyService;
    }
}