using System.Globalization;
using System.Security.Claims;
using Futsal.Application.Abstractions;
using Futsal.Domain.Identity;
using Futsal.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Futsal.Api.Realtime;

public sealed class AvailabilityHub(FutsalDbContext db, IClock clock) : Hub
{
    private const string GroupsKey = "availability-groups";
    private const string GateKey = "availability-groups-gate";
    private const string RateGateKey = "availability-join-rate-gate";
    private const string RateKey = "availability-join-rate";
    private const int MaxGroups = 10;
    private const int MaxJoinCallsPerMinute = 30;

    public async Task JoinVenueDate(string venueSlug, string date)
    {
        CheckJoinRate();
        var day = ParseDate(date);
        var venue = await db.Venues.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(v => v.Slug == venueSlug && v.IsPublic && v.IsActive
                && db.Locations.Any(l => l.Id == v.LocationId && l.IsActive));
        if (venue is null) throw new HubException("Public venue was not found.");
        await AddGroupAsync(AvailabilityGroups.Public(venue.Id, day));
    }

    public async Task LeaveVenueDate(string venueSlug, string date)
    {
        var day = ParseDate(date);
        var venueId = await db.Venues.IgnoreQueryFilters().AsNoTracking()
            .Where(v => v.Slug == venueSlug).Select(v => (Guid?)v.Id).SingleOrDefaultAsync();
        if (venueId is not null) await RemoveGroupAsync(AvailabilityGroups.Public(venueId.Value, day));
    }

    public async Task JoinStaffVenueDate(string venueSlug, string date)
    {
        CheckJoinRate();
        var day = ParseDate(date);
        if (!Guid.TryParse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            || !Guid.TryParse(Context.User?.FindFirstValue("org"), out var organizationId)
            || !Enum.TryParse<OrganizationRole>(Context.User?.FindFirstValue(ClaimTypes.Role), true, out var role)
            || role is not (OrganizationRole.OWNER or OrganizationRole.MANAGER or OrganizationRole.CASHIER))
            throw new HubException("A staff login is required for the staff group.");

        var venue = await db.Venues.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(v => v.Slug == venueSlug && v.IsActive && v.OrganizationId == organizationId);
        if (venue is null) throw new HubException("Venue is outside the authenticated organization.");
        var isMember = await db.Memberships.IgnoreQueryFilters().AnyAsync(m => m.UserId == userId
            && m.OrganizationId == organizationId && m.Role == role && (m.VenueId == null || m.VenueId == venue.Id));
        if (!isMember) throw new HubException("An active staff membership is required.");
        await AddGroupAsync(AvailabilityGroups.Staff(venue.Id, day));
    }

    public async Task LeaveStaffVenueDate(string venueSlug, string date)
    {
        var day = ParseDate(date);
        var venueId = await db.Venues.IgnoreQueryFilters().AsNoTracking()
            .Where(v => v.Slug == venueSlug).Select(v => (Guid?)v.Id).SingleOrDefaultAsync();
        if (venueId is not null) await RemoveGroupAsync(AvailabilityGroups.Staff(venueId.Value, day));
    }

    private async Task AddGroupAsync(string group)
    {
        var gate = (SemaphoreSlim?)Context.Items[GateKey];
        if (gate is null) Context.Items[GateKey] = gate = new SemaphoreSlim(1, 1);
        await gate.WaitAsync(Context.ConnectionAborted);
        try
        {
            var groups = (HashSet<string>?)Context.Items[GroupsKey];
            if (groups is null) Context.Items[GroupsKey] = groups = new HashSet<string>(StringComparer.Ordinal);
            if (groups.Contains(group)) return;
            if (groups.Count >= MaxGroups) throw new HubException("A connection may join at most 10 availability groups.");
            await Groups.AddToGroupAsync(Context.ConnectionId, group, Context.ConnectionAborted);
            groups.Add(group);
        }
        finally { gate.Release(); }
    }

    private async Task RemoveGroupAsync(string group)
    {
        var gate = (SemaphoreSlim?)Context.Items[GateKey];
        if (gate is null) return;
        await gate.WaitAsync(Context.ConnectionAborted);
        try
        {
            var groups = (HashSet<string>?)Context.Items[GroupsKey];
            if (groups?.Remove(group) == true)
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, group, Context.ConnectionAborted);
        }
        finally { gate.Release(); }
    }

    private void CheckJoinRate()
    {
        var gate = Context.Items[RateGateKey] ??= new object();
        lock (gate)
        {
            var now = clock.UtcNow;
            var state = (JoinRateState?)Context.Items[RateKey];
            if (state is null || now - state.WindowStartedAt >= TimeSpan.FromMinutes(1))
                Context.Items[RateKey] = state = new JoinRateState(now, 0);
            state.Count++;
            if (state.Count > MaxJoinCallsPerMinute) throw new HubException("Join calls are rate limited.");
        }
    }

    private static DateOnly ParseDate(string date) => DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
        DateTimeStyles.None, out var day) ? day : throw new HubException("date must use yyyy-MM-dd in the venue timezone.");

    private sealed class JoinRateState(DateTimeOffset windowStartedAt, int count)
    {
        public DateTimeOffset WindowStartedAt { get; } = windowStartedAt;
        public int Count { get; set; } = count;
    }
}

public static class AvailabilityGroups
{
    public static string Public(Guid venueId, DateOnly localDate) => $"venue:{venueId}:date:{localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
    public static string Staff(Guid venueId, DateOnly localDate) => $"{Public(venueId, localDate)}:staff";
}
