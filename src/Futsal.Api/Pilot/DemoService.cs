using Futsal.Application.Abstractions;
using Futsal.Domain.Identity;
using Futsal.Domain.Locations;
using Futsal.Domain.Bookings;
using Futsal.Infrastructure.Persistence;
using Futsal.Api.Identity;
using Microsoft.EntityFrameworkCore;
namespace Futsal.Api.Pilot;

public sealed class DemoService(FutsalDbContext db, ICurrentUser current, IClock clock, IBookingNotifier? notifier = null)
{
    public async Task<Futsal.Contracts.DemoAccess> AccessAsync(Guid org, CancellationToken ct)
    {
        if (!await db.Organizations.AnyAsync(x => x.Id == org && x.IsDemo, ct)) throw new ApiException(404, "NOT_FOUND", "NOT_FOUND");
        var login = "demo-" + Guid.NewGuid().ToString("N") + "@example.invalid"; var password = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)); var user = new AppUser { Email = login, FullName = "DEMO Owner", CreatedAt = clock.UtcNow }; user.PasswordHash = new Microsoft.AspNetCore.Identity.PasswordHasher<AppUser>().HashPassword(user, password); db.Users.Add(user); db.Memberships.Add(new() { OrganizationId = org, UserId = user.Id, Role = OrganizationRole.OWNER }); await db.SaveChangesAsync(ct); return new(org, login, password, await db.Venues.Where(x => x.OrganizationId == org).Select(x => x.Slug).FirstAsync(ct));
    }
    public async Task<Guid> CreateAsync(CancellationToken ct) { await using var tx = await db.Database.BeginTransactionAsync(ct); var org = new Organization { Name = "DEMO — FutsalPro", IsDemo = true, CreatedAt = clock.UtcNow }; db.Organizations.Add(org); await db.SaveChangesAsync(ct); await SeedAsync(org.Id, ct); Audit(org.Id, "DEMO_CREATED"); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return org.Id; }
    public async Task ResetAsync(Guid id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"demo:" + id},0))", ct); var org = await db.Organizations.SingleOrDefaultAsync(x => x.Id == id && x.IsDemo, ct) ?? throw new ApiException(404, "NOT_FOUND", "NOT_FOUND");
        var oldBookings = await db.Bookings.AsNoTracking().Where(x => x.OrganizationId == id && x.Status != BookingStatus.CANCELLED && x.Status != BookingStatus.EXPIRED).ToListAsync(ct);
        // Only demo tenant rows; never relax database booking constraints or touch real organizations.
        await db.WaitlistEntries.Where(x=>x.OrganizationId==id).ExecuteDeleteAsync(ct);
        await db.Notifications.Where(x => x.OrganizationId == id).ExecuteDeleteAsync(ct); await db.Payments.Where(x => x.OrganizationId == id).ExecuteDeleteAsync(ct); await db.Bookings.Where(x => x.OrganizationId == id).ExecuteDeleteAsync(ct); await db.SeriesPrepayments.Where(x=>x.OrganizationId==id).ExecuteDeleteAsync(ct); await db.RecurringSeries.Where(x=>x.OrganizationId==id).ExecuteDeleteAsync(ct); await db.Customers.Where(x => x.OrganizationId == id).ExecuteDeleteAsync(ct);
        var venues = await db.Venues.Where(x => x.OrganizationId == id).ToListAsync(ct); foreach (var v in venues) { v.Name = "DEMO Arena"; v.Address = "DEMO — fictional venue"; v.FreeCancelHours = 6; v.AdvanceType = "PERCENT"; v.AdvanceValue = 30; }
        await db.BlockedPeriods.Where(x => x.OrganizationId == id).ExecuteDeleteAsync(ct); await db.PricingRules.Where(x => x.OrganizationId == id).ExecuteDeleteAsync(ct); await db.CourtHours.Where(h => db.Courts.Any(c => c.Id == h.CourtId && c.OrganizationId == id)).ExecuteDeleteAsync(ct);
        var courts = await db.Courts.Where(x => x.OrganizationId == id).OrderBy(x => x.Name).ToListAsync(ct); var index = 0; foreach (var c in courts) { c.Name = "DEMO Court " + (++index); c.PricePerSlot = 1500; c.SlotMinutes = 60; c.IsActive = true; for (short day = 0; day < 7; day++) db.CourtHours.Add(new() { CourtId = c.Id, Weekday = day, OpenTime = new(6, 0), CloseTime = new(23, 0) }); db.PricingRules.Add(new() { OrganizationId = id, CourtId = c.Id, Name = "DEMO Evening", Weekdays = [0, 1, 2, 3, 4, 5, 6], FromTime = new(17, 0), ToTime = new(22, 0), PricePerSlot = 2000, Priority = 1 }); }
        await SeedBookingsAsync(id, ct); Audit(org.Id, "DEMO_RESET"); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        if (notifier != null) { foreach (var b in oldBookings) await notifier.PublishAsync(new("SlotReleased", b.VenueId, b.CourtId, b.StartAt, b.EndAt, null, b.Id), ct); foreach (var b in await db.Bookings.AsNoTracking().Where(x => x.OrganizationId == id).ToListAsync(ct)) await notifier.PublishAsync(new("SlotBooked", b.VenueId, b.CourtId, b.StartAt, b.EndAt, null, b.Id), ct); }
    }
    async Task SeedAsync(Guid org, CancellationToken ct)
    {
        var city = await db.Locations.FirstOrDefaultAsync(x => x.Type == LocationType.CITY && x.IsActive, ct); if (city == null) { city = new() { NameEn = "DEMO Kathmandu", NameNe = "डेमो काठमाडौं", Slug = "demo-city-" + Guid.NewGuid().ToString("N"), Type = LocationType.CITY, IsSeeded = true }; db.Locations.Add(city); await db.SaveChangesAsync(ct); }
        var venue = new Venue { OrganizationId = org, Name = "DEMO Arena", Slug = "demo-" + Guid.NewGuid().ToString("N"), LocationId = city.Id, CityId = city.Id, Address = "DEMO — fictional venue", IsPublic = true }; db.Venues.Add(venue); db.Subscriptions.Add(new() { OrganizationId = org, Plan = "Demo", Status = "ACTIVE", StartsAt = clock.UtcNow, EndsAt = clock.UtcNow.AddYears(1), MaxCourts = 2 });
        for (int i = 1; i <= 2; i++) { var court = new Court { OrganizationId = org, VenueId = venue.Id, Name = "DEMO Court " + i, PricePerSlot = 1500 }; db.Courts.Add(court); for (short d = 0; d < 7; d++) db.CourtHours.Add(new() { CourtId = court.Id, Weekday = d, OpenTime = new(6, 0), CloseTime = new(23, 0) }); db.PricingRules.Add(new() { OrganizationId = org, CourtId = court.Id, Name = "DEMO Evening", Weekdays = [0, 1, 2, 3, 4, 5, 6], FromTime = new(17, 0), ToTime = new(22, 0), PricePerSlot = 2000, Priority = 1 }); }
        await db.SaveChangesAsync(ct); await SeedBookingsAsync(org, ct);
    }
    async Task SeedBookingsAsync(Guid org, CancellationToken ct)
    {
        var court = await db.Courts.FirstAsync(x => x.OrganizationId == org, ct); var local = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(clock.UtcNow, "Asia/Kathmandu");
        for (int i = 0; i < 5; i++) { var customer = new Customer { OrganizationId = org, FullName = "DEMO Player " + (i + 1), Phone = "+97798000001" + i.ToString("D2"), OptOut = true, CreatedAt = clock.UtcNow }; db.Customers.Add(customer); var booking = Booking.NewStaffConfirmed(); booking.OrganizationId = org; booking.VenueId = court.VenueId; booking.CourtId = court.Id; booking.CustomerId = customer.Id; booking.BookingCode = "DEMO-" + Guid.NewGuid().ToString("N")[..10]; booking.StartAt = new DateTimeOffset(local.Date.AddDays(1).AddHours(17 + i), TimeSpan.FromMinutes(345)).ToUniversalTime(); booking.EndAt = booking.StartAt.AddHours(1); booking.TotalAmount = 2000; booking.Source = BookingSource.STAFF; booking.CreatedAt = clock.UtcNow; booking.UpdatedAt = clock.UtcNow; db.Bookings.Add(booking); }
        await db.SaveChangesAsync(ct);
    }
    void Audit(Guid org, string action) => db.AuditLogs.Add(new() { OrganizationId = org, UserId = current.UserId, Action = action, EntityType = "Organization", EntityId = org, CreatedAt = clock.UtcNow });
}
