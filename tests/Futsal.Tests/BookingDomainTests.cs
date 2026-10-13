using Futsal.Api.Bookings;
using Futsal.Domain.Bookings;
using Futsal.Domain.Locations;
using Futsal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Futsal.Tests;

public sealed class BookingDomainTests
{
    private sealed class TestCurrentUser : Futsal.Application.Abstractions.ICurrentUser
    {
        public Guid? UserId => null;
        public Guid? OrganizationId => null;
        public string? Role => null;
        public bool IsSuperAdmin => true;
        public bool IsAuthenticated => false;
    }

    private sealed class TestClock : Futsal.Application.Abstractions.IClock
    {
        public DateTimeOffset UtcNow => new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    }

    [Fact]
    public void Held_booking_can_confirm_and_cannot_confirm_twice()
    {
        var booking = Booking.NewHeld();
        booking.Confirm();
        Assert.Equal(BookingStatus.CONFIRMED, booking.Status);
        Assert.Null(booking.HoldExpiresAt);
        var error = Assert.Throws<BookingDomainException>(booking.Confirm);
        Assert.Equal("INVALID_BOOKING_TRANSITION", error.Code);
    }

    [Theory]
    [InlineData(BookingStatus.HELD, BookingStatus.CANCELLED)]
    [InlineData(BookingStatus.CONFIRMED, BookingStatus.CANCELLED)]
    [InlineData(BookingStatus.COMPLETED, BookingStatus.CANCELLED)]
    [InlineData(BookingStatus.EXPIRED, BookingStatus.CANCELLED)]
    public void Cancellation_only_applies_to_held_or_confirmed(BookingStatus initial, BookingStatus? expected)
    {
        var booking = initial is BookingStatus.HELD or BookingStatus.EXPIRED ? Booking.NewHeld() : Booking.NewStaffConfirmed();
        if (initial is BookingStatus.COMPLETED or BookingStatus.EXPIRED)
        {
            if (initial == BookingStatus.COMPLETED) booking.Complete();
            else booking.Expire();
            Assert.Throws<BookingDomainException>(() => booking.Cancel(DateTimeOffset.UtcNow));
        }
        else
        {
            booking.Cancel(DateTimeOffset.UtcNow);
            Assert.Equal(expected, booking.Status);
        }
    }

    [Fact]
    public async Task Pricing_resolves_highest_priority_rule_and_regular_customer_discount()
    {
        var options = new DbContextOptionsBuilder<FutsalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new FutsalDbContext(options, new TestCurrentUser(), new TestClock());
        var courtId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        db.PricingRules.AddRange(
            new PricingRule { Id = Guid.NewGuid(), OrganizationId = orgId, CourtId = courtId, Name = "low", Weekdays = [1], FromTime = new TimeOnly(18, 0), ToTime = new TimeOnly(19, 0), PricePerSlot = 1000m, Priority = 1 },
            new PricingRule { Id = Guid.NewGuid(), OrganizationId = orgId, CourtId = courtId, Name = "peak", Weekdays = [1], FromTime = new TimeOnly(18, 0), ToTime = new TimeOnly(19, 0), PricePerSlot = 1500m, Priority = 10 });
        await db.SaveChangesAsync();
        var court = new Court { Id = courtId, OrganizationId = orgId, PricePerSlot = 700m, SlotMinutes = 60 };
        var venue = new Venue { Timezone = "Asia/Kathmandu" };
        var customer = new Customer { IsRegular = true, DiscountPercent = 10m };
        var monday = new DateTimeOffset(2026, 10, 12, 12, 15, 0, TimeSpan.Zero); // 18:00 in Kathmandu.

        var result = await new BookingPricing(db).CalculateAsync(court, monday, monday.AddHours(1), venue, customer, default);

        Assert.Equal(1500m, result.Gross);
        Assert.Equal(150m, result.Discount);
        Assert.Equal(1350m, result.Total);
    }
}
