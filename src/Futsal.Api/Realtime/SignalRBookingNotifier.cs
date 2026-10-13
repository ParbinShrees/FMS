using Futsal.Application.Abstractions;
using Futsal.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Futsal.Api.Realtime;

public sealed record AvailabilityEventPayload(Guid VenueId, Guid CourtId, DateTimeOffset StartUtc,
    DateTimeOffset EndUtc, string Status, DateTimeOffset? HoldExpiresAtUtc);
public sealed record StaffBookingEventPayload(Guid VenueId, Guid CourtId, Guid BookingId, DateTimeOffset StartUtc,
    DateTimeOffset EndUtc, string Status, DateTimeOffset? HoldExpiresAtUtc);

public sealed class SignalRBookingNotifier(
    IHubContext<AvailabilityHub> hub,
    FutsalDbContext db,
    ILogger<SignalRBookingNotifier> logger) : IBookingNotifier
{
    public async Task PublishAsync(BookingEvent message, CancellationToken cancellationToken)
    {
        try
        {
            var venue = await db.Venues.IgnoreQueryFilters().AsNoTracking()
                .Where(v => v.Id == message.VenueId).Select(v => new { v.Id, v.Timezone }).SingleOrDefaultAsync(cancellationToken);
            if (venue is null) return;
            var (eventName, status) = message.Name switch
            {
                "SlotHeld" => ("SlotHeld", "HELD"),
                "SlotReleased" => ("SlotReleased", "FREE"),
                "SlotBooked" => ("SlotBooked", "BOOKED"),
                "SlotBlocked" => ("SlotBlocked", "BLOCKED"),
                "SlotCancelled" => ("SlotCancelled", "CANCELLED"),
                _ => throw new InvalidOperationException($"Unsupported availability event '{message.Name}'.")
            };
            var payload = new AvailabilityEventPayload(venue.Id, message.CourtId, message.StartAt.ToUniversalTime(),
                message.EndAt.ToUniversalTime(), status, message.HoldExpiresAt?.ToUniversalTime());
            var zone = TimeZoneInfo.FindSystemTimeZoneById(venue.Timezone);
            var firstDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(payload.StartUtc, zone).DateTime);
            var lastInstant = payload.EndUtc > payload.StartUtc ? payload.EndUtc.AddTicks(-1) : payload.EndUtc;
            var lastDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(lastInstant, zone).DateTime);
            for (var day = firstDate; day <= lastDate; day = day.AddDays(1))
            {
                await hub.Clients.Group(AvailabilityGroups.Public(venue.Id, day)).SendAsync(eventName, payload, cancellationToken);
                var staffGroup = AvailabilityGroups.Staff(venue.Id, day);
                await hub.Clients.Group(staffGroup).SendAsync(eventName, payload, cancellationToken);
                if (message.BookingId is Guid bookingId)
                {
                    var staffPayload = new StaffBookingEventPayload(venue.Id, message.CourtId, bookingId, payload.StartUtc,
                        payload.EndUtc, payload.Status, payload.HoldExpiresAtUtc);
                    await hub.Clients.Group(staffGroup).SendAsync("StaffBookingChanged", staffPayload, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Realtime availability publish failed for venue {VenueId} and court {CourtId}; the committed booking remains successful.", message.VenueId, message.CourtId);
        }
    }
}
