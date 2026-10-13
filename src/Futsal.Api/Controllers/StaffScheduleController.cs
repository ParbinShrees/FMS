using Futsal.Api.Bookings;
using Futsal.Api.Contracts;
using Futsal.Api.Identity;
using Futsal.Application.Abstractions;
using Futsal.Domain.Bookings;
using Futsal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Futsal.Api.Controllers;

[ApiController, Route("api/v1/staff"), Authorize(Policy = "StaffAccess")]
public sealed class StaffScheduleController(FutsalDbContext db, BookingPricing pricing, IClock clock) : ControllerBase
{
    [HttpGet("venues")]
    public async Task<IActionResult> Venues(CancellationToken ct) => Ok(await db.Venues.AsNoTracking()
        .Where(v => v.IsActive).OrderBy(v => v.Name).Select(v => new { v.Id, v.Name, v.Slug, v.Timezone }).ToListAsync(ct));

    [HttpGet("venues/{slug}/availability")]
    public async Task<IActionResult> Availability(string slug, [FromQuery] DateOnly? date, CancellationToken ct)
    {
        if (date is null) throw new ApiException(400, "VALIDATION_FAILED", "date is required in YYYY-MM-DD format.");
        var venue = await db.Venues.AsNoTracking().SingleOrDefaultAsync(v => v.Slug == slug && v.IsActive, ct)
            ?? throw new ApiException(404, "NOT_FOUND", "Venue was not found.");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(venue.Timezone);
        var weekday = (short)date.Value.DayOfWeek;
        var courts = await db.Courts.Where(c => c.VenueId == venue.Id && c.IsActive).ToListAsync(ct);
        var output = new List<AvailabilitySlot>();
        foreach (var court in courts)
        {
            var hours = await db.CourtHours.AsNoTracking().SingleOrDefaultAsync(h => h.CourtId == court.Id && h.Weekday == weekday, ct);
            if (hours is null) continue;
            var open = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(date.Value.ToDateTime(hours.OpenTime), DateTimeKind.Unspecified), zone), TimeSpan.Zero);
            var close = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(date.Value.ToDateTime(hours.CloseTime), DateTimeKind.Unspecified), zone), TimeSpan.Zero);
            var bookings = await db.Bookings.Where(b => b.CourtId == court.Id && b.StartAt < close && b.EndAt > open)
                .Select(b => new { b.StartAt, b.EndAt, b.Status, b.HoldExpiresAt }).ToListAsync(ct);
            var blocks = await db.BlockedPeriods.Where(b => b.CourtId == court.Id && b.StartAt < close && b.EndAt > open).ToListAsync(ct);
            for (var start = open; start.AddMinutes(court.SlotMinutes) <= close; start = start.AddMinutes(court.SlotMinutes))
            {
                var end = start.AddMinutes(court.SlotMinutes);
                var status = blocks.Any(b => b.StartAt < end && b.EndAt > start) ? "BLOCKED" : "FREE";
                var booking = bookings.FirstOrDefault(b => b.StartAt < end && b.EndAt > start && (b.Status != BookingStatus.HELD || b.HoldExpiresAt > clock.UtcNow));
                if (booking is not null) status = booking.Status == BookingStatus.HELD ? "HELD" : booking.Status.ToString();
                var price = await pricing.CalculateAsync(court, start, end, venue, null, ct);
                output.Add(new AvailabilitySlot(court.Id, court.Name, start, end, status, price.Total));
            }
        }
        return Ok(output);
    }

    [HttpPost("bookings/quote")]
    public async Task<IActionResult> Quote(CreateHoldRequest request, CancellationToken ct)
    {
        var court = await db.Courts.Include(c => c.Venue).SingleOrDefaultAsync(c => c.Id == request.CourtId, ct)
            ?? throw new ApiException(404, "NOT_FOUND", "Court was not found.");
        if (request.EndAt <= request.StartAt) throw new ApiException(400, "VALIDATION_FAILED", "endAt must be after startAt.");
        var result = await pricing.CalculateAsync(court, request.StartAt.ToUniversalTime(), request.EndAt.ToUniversalTime(), court.Venue, null, ct);
        var advance = court.Venue.AdvanceType == "FIXED" ? Math.Min(result.Total, court.Venue.AdvanceValue) : decimal.Round(result.Total * court.Venue.AdvanceValue / 100m, 2);
        return Ok(new { totalAmount = result.Total, discountAmount = result.Discount, advanceRequired = advance });
    }
}
