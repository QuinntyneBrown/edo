﻿using System.Collections.Generic;
using HappyTravel.Edo.Api.Models.Accommodations;
using HappyTravel.Edo.Common.Enums;
using HappyTravel.Edo.Data.Booking;
using HappyTravel.Money.Models;
using Newtonsoft.Json;

namespace HappyTravel.Edo.Api.Models.Bookings
{
    public readonly struct AccommodationBookingInfo
    {
        [JsonConstructor]
        public AccommodationBookingInfo(int bookingId, AccommodationBookingDetails bookingDetails, int counterpartyId,
            BookingPaymentStatuses paymentStatus, MoneyAmount totalPrice, List<BookedRoom> rooms)
        {
            BookingId = bookingId;
            BookingDetails = bookingDetails;
            CounterpartyId = counterpartyId;
            PaymentStatus = paymentStatus;
            TotalPrice = totalPrice;
        }


        public override bool Equals(object obj) => obj is AccommodationBookingInfo other && Equals(other);


        public bool Equals(AccommodationBookingInfo other)
            => Equals((BookingId, BookingDetails, CounterpartyId, PaymentStatus, TotalPrice),
                (other.BookingId, other.BookingDetails, other.CounterpartyId, other.PaymentStatus, TotalPrice));


        public override int GetHashCode() => (BookingId, BookingDetails, CounterpartyId: CounterpartyId).GetHashCode();


        public int BookingId { get; }
        public AccommodationBookingDetails BookingDetails { get; }
        public int CounterpartyId { get; }
        public BookingPaymentStatuses PaymentStatus { get; }
        public MoneyAmount TotalPrice { get; }
    }
}