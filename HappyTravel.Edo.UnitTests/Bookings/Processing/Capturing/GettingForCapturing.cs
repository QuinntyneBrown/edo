using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HappyTravel.Edo.Api.Infrastructure;
using HappyTravel.Edo.Api.Services.Accommodations.Bookings;
using HappyTravel.Edo.Api.Services.Payments;
using HappyTravel.Edo.Common.Enums;
using HappyTravel.Edo.Data.Booking;
using HappyTravel.Edo.UnitTests.Infrastructure;
using HappyTravel.Edo.UnitTests.Infrastructure.DbSetMocks;
using HappyTravel.EdoContracts.Accommodations.Enums;
using HappyTravel.EdoContracts.General.Enums;
using Moq;
using Xunit;

namespace HappyTravel.Edo.UnitTests.Bookings.Processing.Capturing
{
    public class GettingForCapturing
    {
        [Fact]
        public async Task Should_return_bookings_within_given_deadline()
        {
            var date = new DateTime(2021, 12, 3);
            var bookings = new []
            {
                CreateBooking(id: 1, deadlineDate: new DateTime(2021, 12, 1)),
                CreateBooking(id: 2, deadlineDate: new DateTime(2021, 12, 2)),
                CreateBooking(id: 3, deadlineDate: new DateTime(2021, 12, 3)),
                CreateBooking(id: 4, deadlineDate: new DateTime(2021, 12, 4)),
                CreateBooking(id: 5, deadlineDate: null)
            };
            var service = CreateProcessingService(bookings);

            var bookingsToCapture = await service.GetForCapture(date);

            Assert.Contains(1, bookingsToCapture);
            Assert.Contains(2, bookingsToCapture);
            Assert.Contains(3, bookingsToCapture);
            Assert.DoesNotContain(4, bookingsToCapture);
            Assert.DoesNotContain(5, bookingsToCapture);

            static Booking CreateBooking(int id, DateTime? deadlineDate) => new Booking
            {
                Id = id, 
                PaymentStatus = BookingPaymentStatuses.Authorized,
                Status = BookingStatusCodes.Confirmed,
                PaymentMethod = PaymentMethods.BankTransfer,
                DeadlineDate = deadlineDate,
                CheckInDate = DateTime.MaxValue
            };
        }
        
        
        [Fact]
        public async Task Should_return_bookings_within_given_checkin_date()
        {
            var date = new DateTime(2021, 12, 2);
            var bookings = new []
            {
                CreateBooking(id: 1, checkInDate: new DateTime(2021, 12, 1)),
                CreateBooking(id: 2, checkInDate: new DateTime(2021, 12, 2)),
                CreateBooking(id: 3, checkInDate: new DateTime(2021, 12, 3)),
                CreateBooking(id: 4, checkInDate: new DateTime(2021, 12, 4)),
            };
            var service = CreateProcessingService(bookings);

            var bookingsToCapture = await service.GetForCapture(date);

            Assert.Contains(1, bookingsToCapture);
            Assert.Contains(2, bookingsToCapture);
            Assert.DoesNotContain(3, bookingsToCapture);
            Assert.DoesNotContain(4, bookingsToCapture);

            static Booking CreateBooking(int id, DateTime checkInDate) => new Booking
            {
                Id = id, 
                PaymentStatus = BookingPaymentStatuses.Authorized,
                Status = BookingStatusCodes.Confirmed,
                PaymentMethod = PaymentMethods.BankTransfer,
                DeadlineDate = null,
                CheckInDate = checkInDate
            };
        }
        
        
        [Fact]
        public async Task Should_return_bookings_within_valid_payment_methods()
        {
            var date = new DateTime(2021, 12, 2);
            var bookings = new []
            {
                CreateBooking(id: 1, paymentMethod: PaymentMethods.Offline),
                CreateBooking(id: 2, paymentMethod: PaymentMethods.Other),
                CreateBooking(id: 3, paymentMethod: PaymentMethods.BankTransfer),
                CreateBooking(id: 4, paymentMethod: PaymentMethods.CreditCard),
            };
            var service = CreateProcessingService(bookings);

            var bookingsToCapture = await service.GetForCapture(date);

            Assert.DoesNotContain(1, bookingsToCapture);
            Assert.DoesNotContain(2, bookingsToCapture);
            Assert.Contains(3, bookingsToCapture);
            Assert.Contains(4, bookingsToCapture);

            static Booking CreateBooking(int id, PaymentMethods paymentMethod) => new Booking
            {
                Id = id, 
                PaymentStatus = BookingPaymentStatuses.Authorized,
                Status = BookingStatusCodes.Confirmed,
                PaymentMethod = paymentMethod,
                DeadlineDate = DateTime.MinValue,
                CheckInDate = DateTime.MinValue
            };
        }
        
        
        [Fact]
        public async Task Should_return_authorized_bookings()
        {
            var date = new DateTime(2021, 12, 2);
            var bookings = new []
            {
                CreateBooking(id: 1, paymentStatus: BookingPaymentStatuses.Authorized),
                CreateBooking(id: 2, paymentStatus: BookingPaymentStatuses.Captured),
                CreateBooking(id: 3, paymentStatus: BookingPaymentStatuses.Refunded),
                CreateBooking(id: 4, paymentStatus: BookingPaymentStatuses.Voided),
                CreateBooking(id: 5, paymentStatus: BookingPaymentStatuses.NotPaid)
            };
            var service = CreateProcessingService(bookings);

            var bookingsToCapture = await service.GetForCapture(date);

            Assert.Contains(1, bookingsToCapture);
            Assert.DoesNotContain(2, bookingsToCapture);
            Assert.DoesNotContain(3, bookingsToCapture);
            Assert.DoesNotContain(4, bookingsToCapture);
            Assert.DoesNotContain(5, bookingsToCapture);

            static Booking CreateBooking(int id, BookingPaymentStatuses paymentStatus) => new Booking
            {
                Id = id, 
                PaymentStatus = paymentStatus,
                Status = BookingStatusCodes.Confirmed,
                PaymentMethod = PaymentMethods.CreditCard,
                DeadlineDate = DateTime.MinValue,
                CheckInDate = DateTime.MinValue
            };
        }

        
        [Fact]
        public async Task Should_return_confirmed_or_pending_bookings()
        {
            var date = new DateTime(2021, 12, 2);
            var bookings = new []
            {
                CreateBooking(id: 1, statusCode: BookingStatusCodes.Cancelled),
                CreateBooking(id: 2, statusCode: BookingStatusCodes.Confirmed),
                CreateBooking(id: 3, statusCode: BookingStatusCodes.Invalid),
                CreateBooking(id: 4, statusCode: BookingStatusCodes.Pending),
                CreateBooking(id: 5, statusCode: BookingStatusCodes.Rejected),
                CreateBooking(id: 6, statusCode: BookingStatusCodes.InternalProcessing),
                CreateBooking(id: 7, statusCode: BookingStatusCodes.WaitingForResponse)
            };
            var service = CreateProcessingService(bookings);

            var bookingsToCapture = await service.GetForCapture(date);

            Assert.DoesNotContain(1, bookingsToCapture);
            Assert.Contains(2, bookingsToCapture);
            Assert.DoesNotContain(3, bookingsToCapture);
            Assert.Contains(4, bookingsToCapture);
            Assert.DoesNotContain(5, bookingsToCapture);
            Assert.Contains(6, bookingsToCapture);
            Assert.Contains(7, bookingsToCapture);

            static Booking CreateBooking(int id, BookingStatusCodes statusCode) => new Booking
            {
                Id = id, 
                PaymentStatus = BookingPaymentStatuses.Authorized,
                Status = statusCode,
                PaymentMethod = PaymentMethods.CreditCard,
                DeadlineDate = DateTime.MinValue,
                CheckInDate = DateTime.MinValue
            };
        }
        

        private BookingsProcessingService CreateProcessingService(IEnumerable<Booking> bookings)
        {
            var context = MockEdoContext.Create();
            context.Setup(c => c.Bookings)
                .Returns(DbSetMockProvider.GetDbSetMock(bookings));

            var service = new BookingsProcessingService(Mock.Of<IBookingPaymentService>(),
                Mock.Of<IPaymentNotificationService>(),
                Mock.Of<IBookingService>(),
                context.Object);
            
            return service;
        }
    }
}