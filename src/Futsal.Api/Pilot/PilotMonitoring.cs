using Futsal.Application.Abstractions;
using Futsal.Contracts;
using Futsal.Domain.Bookings;
using Futsal.Domain.Pilot;
using Futsal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace Futsal.Api.Pilot;

public sealed class PilotMonitoring(FutsalDbContext db, IClock clock)
{
    public async Task<List<PilotSummary>> SummariesAsync(Guid? venueId, CancellationToken ct)
    {
        var orgs = await db.Organizations.AsNoTracking().ToListAsync(ct); var result = new List<PilotSummary>(); var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(clock.UtcNow, "Asia/Kathmandu").DateTime); var from = new DateTimeOffset(today.AddDays(-6).ToDateTime(TimeOnly.MinValue), TimeSpan.FromMinutes(345)).ToUniversalTime();
        foreach (var org in orgs)
        {
            var venue = venueId == null ? null : await db.Venues.AsNoTracking().SingleOrDefaultAsync(x => x.Id == venueId && x.OrganizationId == org.Id, ct); if (venueId != null && venue == null) continue;
            var rows = await db.Bookings.AsNoTracking().Where(x => x.OrganizationId == org.Id && (venueId == null || x.VenueId == venueId) && x.StartAt >= from && x.StartAt < from.AddDays(7)).Select(x => new { x.StartAt, x.Source, x.Status }).ToListAsync(ct);
            var days = Enumerable.Range(0, 7).Select(i => { var day = today.AddDays(i - 6); var items = rows.Where(x => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(x.StartAt, "Asia/Kathmandu").DateTime) == day).ToArray(); return new PilotDay(day, items.Length, items.Count(x => x.Source == BookingSource.ONLINE), items.Count(x => x.Source == BookingSource.STAFF), items.Count(x => x.Status == BookingStatus.NO_SHOW), items.Count(x => x.Status == BookingStatus.CANCELLED)); }).ToList();
            var watch = await db.BookingWatches.SingleOrDefaultAsync(x => x.OrganizationId == org.Id, ct); var since = clock.UtcNow.AddHours(-24);
            result.Add(new(org.Id, org.Name, venueId, venue?.Name, org.IsDemo, days, await db.Payments.CountAsync(x => x.OrganizationId == org.Id && x.Status == PaymentStatus.FAILED && x.CreatedAt >= from && (venueId == null || db.Bookings.Any(b => b.Id == x.BookingId && b.VenueId == venueId)), ct), await db.Notifications.CountAsync(x => x.OrganizationId == org.Id && x.Status == "SENT" && x.CreatedAt >= from && (venueId == null || x.VenueId == venueId), ct), await db.Notifications.CountAsync(x => x.OrganizationId == org.Id && x.Status == "FAILED" && x.CreatedAt >= from && (venueId == null || x.VenueId == venueId), ct), await db.AuditLogs.Where(x => x.OrganizationId == org.Id && x.Action == "LOGIN_SUCCEEDED").MaxAsync(x => (DateTimeOffset?)x.CreatedAt, ct), await db.PilotErrors.CountAsync(x => x.OrganizationId == org.Id && x.CreatedAt >= since, ct), watch?.OverlapCount ?? 0, watch?.CheckedAt));
        }
        return result;
    }
    public async Task WatchAsync(CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"pilot-watchdog"},0))", ct);
        var orgs = await db.Organizations.IgnoreQueryFilters().Select(x => x.Id).ToListAsync(ct);
        foreach (var org in orgs)
        {
            var state = await db.BookingWatches.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.OrganizationId == org, ct); if (state != null && state.CheckedAt > clock.UtcNow.AddDays(-1)) continue;
            var active = db.Bookings.IgnoreQueryFilters().Where(x => x.OrganizationId == org && x.Status != BookingStatus.CANCELLED && x.Status != BookingStatus.EXPIRED);
            var count = await (from a in active from b in active where a.Id.CompareTo(b.Id) < 0 && a.CourtId == b.CourtId && a.StartAt < b.EndAt && b.StartAt < a.EndAt select a.Id).LongCountAsync(ct);
            if (state == null) { state = new() { OrganizationId = org }; db.BookingWatches.Add(state); }
            state.OverlapCount = count; state.CheckedAt = clock.UtcNow;
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}
public sealed class PilotWatchdogJob(IServiceScopeFactory scopes, ILogger<PilotWatchdogJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct) { using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5)); do { try { await using var scope = scopes.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<PilotMonitoring>().WatchAsync(ct); } catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; } catch (Exception ex) { logger.LogError(ex, "Pilot watchdog failed"); } } while (await timer.WaitForNextTickAsync(ct)); }
}
