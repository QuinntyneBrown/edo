using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HappyTravel.Edo.Api.Services.PriceProcessing;
using HappyTravel.EdoContracts.Accommodations;
using HappyTravel.EdoContracts.Accommodations.Internals;
using HappyTravel.EdoContracts.General;
using HappyTravel.Money.Enums;

namespace HappyTravel.Edo.Api.Services.Accommodations.Availability
{
    public static class AvailabilityResultsExtensions
    {
        public static async ValueTask<AccommodationAvailability> ProcessPrices(this AccommodationAvailability source,
            PriceProcessFunction processFunction)
        {
            var roomContractSets = await ProcessRoomContractSetsPrices(source.RoomContractSets, processFunction);
            return new AccommodationAvailability(source.AvailabilityId,
                source.CheckInDate,
                source.CheckOutDate,
                source.NumberOfNights,
                source.Accommodation,
                roomContractSets);
        }


        public static async ValueTask<RoomContractSetAvailability?> ProcessPrices(
            this RoomContractSetAvailability? source,
            PriceProcessFunction processFunction)
        {
            if (source == null)
                return null;

            var value = source.Value;
            var roomContractSet = await ProcessRoomContractSetPrice(value.RoomContractSet, processFunction);
            return new RoomContractSetAvailability(value.AvailabilityId,
                value.CheckInDate,
                value.CheckOutDate,
                value.NumberOfNights,
                value.Accommodation,
                roomContractSet);
        }


        private static async Task<List<RoomContractSet>> ProcessRoomContractSetsPrices(List<RoomContractSet> sourceRoomContractSets, PriceProcessFunction priceProcessFunction)
        {
            var roomContractSets = new List<RoomContractSet>(sourceRoomContractSets.Count);
            foreach (var roomContractSet in sourceRoomContractSets)
            {
                var roomContractSetWithMarkup = await ProcessRoomContractSetPrice(roomContractSet, priceProcessFunction);
                roomContractSets.Add(roomContractSetWithMarkup);
            }

            return roomContractSets;
        }


        private static async Task<RoomContractSet> ProcessRoomContractSetPrice(RoomContractSet sourceRoomContractSet, PriceProcessFunction priceProcessFunction)
        {
            var currency = sourceRoomContractSet.Price.Currency;

            var roomContracts = new List<RoomContract>(sourceRoomContractSet.RoomContracts.Count);
            foreach (var room in sourceRoomContractSet.RoomContracts)
            {
                var roomPrices = new List<DailyPrice>(room.RoomPrices.Count);
                foreach (var roomPrice in room.RoomPrices)
                {
                    var (roomGross, roomCurrency) = await priceProcessFunction(roomPrice.Gross, currency);
                    var (roomNetTotal, _) = await priceProcessFunction(roomPrice.NetTotal, currency);

                    roomPrices.Add(BuildDailyPrice(roomPrice, roomNetTotal, roomGross, roomCurrency));
                }

                var totalPriceGross = await priceProcessFunction(room.TotalPrice.NetTotal, currency);
                var totalPriceNet = await priceProcessFunction(room.TotalPrice.Gross, currency);
                var totalPrice = new Price(totalPriceGross.Currency, totalPriceNet.Amount, totalPriceGross.Amount);

                roomContracts.Add(BuildRoomContracts(room, roomPrices, totalPrice));
            }

            var (roomContractSetGross, roomContractSetCurrency) = await priceProcessFunction(sourceRoomContractSet.Price.Gross, currency);
            var (roomContractSetNetTotal, _) = await priceProcessFunction(sourceRoomContractSet.Price.NetTotal, currency);
            var roomContractSetPrice = new Price(roomContractSetCurrency, roomContractSetNetTotal, roomContractSetGross, sourceRoomContractSet.Price.Discounts,
                sourceRoomContractSet.Price.Type, sourceRoomContractSet.Price.Description);

            return BuildRoomContractSet(sourceRoomContractSet, roomContractSetPrice, roomContracts);


            static DailyPrice BuildDailyPrice(in DailyPrice roomPrice, decimal roomNetTotal, decimal roomGross, Currencies roomCurrency)
                => new DailyPrice(roomPrice.FromDate, roomPrice.ToDate, roomCurrency, roomNetTotal, roomGross, roomPrice.Type, roomPrice.Description);


            static RoomContract BuildRoomContracts(in RoomContract room, List<DailyPrice> roomPrices, Price totalPrice)
                => new RoomContract(room.BoardBasis, 
                    room.MealPlan, 
                    room.DeadlineDate,
                    room.ContractType,
                    room.IsAvailableImmediately,
                    room.IsDynamic,
                    room.ContractDescription,
                    room.Remarks,
                    roomPrices, 
                    totalPrice,
                    room.AdultsNumber, 
                    room.ChildrenAges,
                    room.Type,
                    room.IsExtraBedNeeded,
                    room.Deadline);

            static RoomContractSet BuildRoomContractSet(in RoomContractSet roomContractSet, in Price roomContractSetPrice, List<RoomContract> rooms)
                => new RoomContractSet(roomContractSet.Id, roomContractSetPrice,roomContractSet.Deadline, rooms);
        }


        public static Currencies? GetCurrency(this AccommodationAvailability availabilityDetails)
        {
            if (!availabilityDetails.RoomContractSets.Any())
                return null;
            
            return availabilityDetails.RoomContractSets
                .Select(a => a.Price.Currency)
                .First();
        }
        
        public static Currencies? GetCurrency(this RoomContractSetAvailability? availabilityDetails)
        {
            return availabilityDetails?.RoomContractSet.Price.Currency;
        }


        public static async ValueTask<EdoContracts.Accommodations.Availability> ProcessPrices(EdoContracts.Accommodations.Availability details, PriceProcessFunction processFunction)
        {
            var accommodationAvailabilities = new List<SlimAccommodationAvailability>(details.Results.Count);
            foreach (var supplierResponse in details.Results)
            {
                var convertedAccommodationAvailability = await ProcessAccommodationAvailability(supplierResponse, processFunction);
                accommodationAvailabilities.Add(convertedAccommodationAvailability);
            }

            return new EdoContracts.Accommodations.Availability(details.AvailabilityId, details.NumberOfNights, details.CheckInDate, details.CheckOutDate, accommodationAvailabilities, details.NumberOfProcessedAccommodations);
        }


        public static async ValueTask<SlimAccommodationAvailability> ProcessPrices(SlimAccommodationAvailability accommodationAvailability, PriceProcessFunction function)
        {
            return await ProcessAccommodationAvailability(accommodationAvailability, function);
        }


        public static Currencies? GetCurrency(SlimAccommodationAvailability accommodationAvailability)
        {
            if (!accommodationAvailability.RoomContractSets.Any())
                return null;

            return accommodationAvailability.RoomContractSets.First().Price.Currency;
        }
        
        
        private static async Task<SlimAccommodationAvailability> ProcessAccommodationAvailability(SlimAccommodationAvailability supplierResponse, PriceProcessFunction function)
        {
            var supplierRoomContractSets = supplierResponse.RoomContractSets;
            var roomContractSetsWithMarkup = await ProcessRoomContractSetsPrices(supplierRoomContractSets, function);
            var convertedAccommodationAvailability = new SlimAccommodationAvailability(supplierResponse.Accommodation,
                roomContractSetsWithMarkup,
                supplierResponse.AvailabilityId);
            
            return convertedAccommodationAvailability;
        }
    }
}