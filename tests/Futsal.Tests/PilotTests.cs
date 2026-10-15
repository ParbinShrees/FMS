using Futsal.Api.Pilot;
using Futsal.Api.Bookings;
using Futsal.Api.Identity;
using Futsal.Api.Payments;
using Futsal.Api.Contracts;
using Futsal.Application.Abstractions;
using Futsal.Application.Pilot;
using Futsal.Contracts;
using Futsal.Domain.Bookings;
using Futsal.Domain.Identity;
using Futsal.Domain.Locations;
using Futsal.Domain.Pilot;
using Futsal.Infrastructure.Persistence;
using Futsal.Infrastructure.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using System.Text;
using System.IO.Compression;
using System.Buffers.Binary;
namespace Futsal.Tests;

public sealed class PilotTests(Milestone8PostgresFixture fixture) : IClassFixture<Milestone8PostgresFixture>
{
    static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-15T00:00:00Z"), Start = DateTimeOffset.Parse("2026-10-16T13:15:00Z");
    sealed class User(Guid id, Guid? org = null, string role = "Owner", bool authenticated = true) : ICurrentUser { public Guid? UserId => id; public Guid? OrganizationId => org; public string? Role => role; public bool IsAuthenticated => authenticated; public bool IsSuperAdmin => role == "SuperAdmin"; }
    FutsalDbContext Db(ICurrentUser? user = null) => new(fixture.Options, user ?? new SuperAdminUser(), new FixedClock(Now));
    BookingService Booking(FutsalDbContext db, ICurrentUser user) => new(db, user, new FixedClock(Now), new BookingPricing(db), new NoopBookingNotifier(), NullLogger<BookingService>.Instance);
    PilotImportService Imports(FutsalDbContext db, ICurrentUser user) => new(db, Booking(db, user), user, new FixedClock(Now));
    async Task<(Guid Org, Guid Owner, Guid Court, Guid Venue, Guid Location)> Seed()
    {
        await using var db = Db(); var org = new Organization { Name = "Pilot", CreatedAt = Now }; var owner = new AppUser { FullName = "Pilot owner", CreatedAt = Now }; var city = new Location { Type = LocationType.CITY, NameEn = "City", NameNe = "City", Slug = Guid.NewGuid().ToString(), IsSeeded = true }; db.Organizations.Add(org); db.Users.Add(owner); db.Locations.Add(city); await db.SaveChangesAsync(); db.Memberships.Add(new() { OrganizationId = org.Id, UserId = owner.Id, Role = OrganizationRole.OWNER }); db.Subscriptions.Add(new() { OrganizationId = org.Id, Status = "ACTIVE", StartsAt = Now.AddDays(-1), EndsAt = Now.AddDays(30), MaxCourts = 5 }); var venue = new Venue { OrganizationId = org.Id, Name = "Pilot", Slug = Guid.NewGuid().ToString(), LocationId = city.Id, CityId = city.Id }; db.Venues.Add(venue); var court = new Court { OrganizationId = org.Id, VenueId = venue.Id, Name = "Court A", PricePerSlot = 1500 }; db.Courts.Add(court); for (short d = 0; d < 7; d++) db.CourtHours.Add(new() { CourtId = court.Id, Weekday = d, OpenTime = new(6, 0), CloseTime = new(23, 0) }); await db.SaveChangesAsync(); return (org.Id, owner.Id, court.Id, venue.Id, city.Id);
    }
    [Fact]
    public async Task Customer_import_previews_reports_duplicates_invalid_numbers_and_formula_cells()
    {
        var s = await Seed(); var user = new User(s.Owner, s.Org); await using var db = Db(user); var service = Imports(db, user); var csv = "name,phone\nPlayer,+9779800000000\nDuplicate,9800000000\nInvalid,+919800000000\n=HYPERLINK(),+9779800000001\n\"Quoted, Player\",+9779800000002"; var preview = await service.RunAsync(new(csv, true, Guid.NewGuid()), false, default); Assert.Equal(2, preview.Rows.Count(x => x.Status == "VALID")); Assert.Empty(await db.Customers.ToListAsync());
        var report = await service.RunAsync(new(csv, false, Guid.NewGuid()), false, default); Assert.Equal(2, report.Imported); Assert.Equal(new[] { "DUPLICATE_PHONE", "INVALID_PHONE", "FORMULA_CELL" }, report.Rows.Where(x => x.Status == "SKIPPED").Select(x => x.Code)); Assert.StartsWith("'", report.Rows[3].Cells[0]); Assert.Equal(2, await db.Customers.CountAsync()); Assert.Contains(await db.AuditLogs.ToListAsync(), x => x.Action == "CUSTOMER_IMPORT");
        await Assert.ThrowsAsync<ApiException>(() => service.RunAsync(new(new string('a', 50000), true, Guid.NewGuid()), false, default));
    }
    [Fact]
    public async Task Booking_import_uses_normal_service_rejects_overlaps_and_retries_without_duplicates()
    {
        var s = await Seed(); var user = new User(s.Owner, s.Org); await using var db = Db(user); var service = Imports(db, user); var csv = $"court,start,end,customer phone,amount paid\n{s.Court},{Start:O},{Start.AddHours(1):O},+9779800000021,500\n{s.Court},{Start:O},{Start.AddHours(1):O},+9779800000022,0\n{s.Court},{Start.AddHours(1):O},{Start.AddHours(2):O},+9779800000023,0";
        var run = Guid.NewGuid(); var preview = await service.RunAsync(new(csv, true, run), true, default); Assert.Equal("SLOT_UNAVAILABLE", preview.Rows[1].Code); Assert.Empty(await db.Bookings.ToListAsync());
        var report = await service.RunAsync(new(csv, false, run), true, default); Assert.Equal(2, report.Imported); Assert.Equal("SLOT_UNAVAILABLE", report.Rows[1].Code); Assert.Equal(500, await db.Payments.SumAsync(x => x.Amount)); await service.RunAsync(new(csv, false, run), true, default); Assert.Equal(2, await db.Bookings.CountAsync()); Assert.Equal(500, await db.Payments.SumAsync(x => x.Amount));
    }
    sealed class Sender : INotificationSender { public int Calls; public Task SendAsync(NotificationMessage m, CancellationToken ct) { Calls++; throw new InvalidOperationException("Real SMS must not be called"); } }
    sealed class Gateways : IPaymentGatewayResolver { public int Calls; public IPaymentGateway GetGateway(PaymentMethod method) { Calls++; throw new InvalidOperationException("Real gateway must not be called"); } }
    sealed class Protector : IVenuePaymentProtector { public string Protect(string value) => value; public string Unprotect(string value) => throw new InvalidOperationException("Secrets must not be read"); }
    [Fact]
    public async Task Demo_never_sends_SMS_or_calls_gateway_and_reset_cannot_target_real_tenant()
    {
        var real = await Seed(); Guid demo; await using (var db = Db()) { var service = new DemoService(db, new SuperAdminUser(), new FixedClock(Now)); demo = await service.CreateAsync(default); var access = await service.AccessAsync(demo, default); var demoOwner = await db.Users.SingleAsync(x => x.Email == access.Login); Assert.NotEqual(access.Password, demoOwner.PasswordHash); Assert.NotEqual(PasswordVerificationResult.Failed, new PasswordHasher<AppUser>().VerifyHashedPassword(demoOwner, demoOwner.PasswordHash!, access.Password)); Assert.Equal(OrganizationRole.OWNER, (await db.Memberships.SingleAsync(x => x.UserId == demoOwner.Id)).Role); await service.ResetAsync(demo, default); Assert.Equal(5, await db.Bookings.CountAsync(x => x.OrganizationId == demo)); await Assert.ThrowsAsync<ApiException>(() => service.ResetAsync(real.Org, default)); }
        Guid player, bookingId;
        await using (var db = Db())
        {
            var c = await db.Customers.FirstAsync(x => x.OrganizationId == demo); var u = new AppUser { FullName = "Demo viewer", CreatedAt = Now }; db.Users.Add(u); c.UserId = u.Id; player = u.Id; var court = await db.Courts.FirstAsync(x => x.OrganizationId == demo); var b = Futsal.Domain.Bookings.Booking.NewHeld(); b.OrganizationId = demo; b.VenueId = court.VenueId; b.CourtId = court.Id; b.CustomerId = c.Id; b.BookingCode = Guid.NewGuid().ToString(); b.StartAt = Start.AddDays(2); b.EndAt = b.StartAt.AddHours(1); b.HoldExpiresAt = Now.AddMinutes(10); b.CreatedAt = Now; b.UpdatedAt = Now; b.TotalAmount = 1500; db.Bookings.Add(b); bookingId = b.Id; db.Payments.Add(new() { OrganizationId = demo, BookingId = b.Id, Method = PaymentMethod.ESEWA, Status = PaymentStatus.PENDING, Amount = 500, GatewayTxnUuid = "demo-" + b.Id, CreatedAt = Now.AddMinutes(-5) }); db.Notifications.Add(new() { OrganizationId = demo, VenueId = court.VenueId, Recipient = "+9779800000000", UniqueKey = Guid.NewGuid().ToString(), Template = "confirmation", Payload = "not decrypted", CreatedAt = Now, ScheduledFor = Now }); await db.SaveChangesAsync(); var sender = new Sender(); await new NotificationQueue(db, new FixedClock(Now), new EphemeralDataProtectionProvider(), sender).SendQueuedAsync(default); Assert.Equal(0, sender.Calls); Assert.Contains(await db.Notifications.Where(x => x.OrganizationId == demo).ToListAsync(), x => x.Error == "DEMO_SUPPRESSED");
        }
        var user = new User(player, null, "Customer"); await using (var db = Db(user)) { var gateways = new Gateways(); var service = new PaymentProcessingService(db, user, new FixedClock(Now), gateways, new Protector(), new NoopBookingNotifier(), NullLogger<PaymentProcessingService>.Instance); var ex = await Assert.ThrowsAsync<ApiException>(() => service.InitiateEsewaAsync(new(bookingId), null, default)); Assert.Equal("DEMO_GATEWAY_DISABLED", ex.Code); var data = Convert.ToBase64String(Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new { transaction_uuid = "demo-" + bookingId }))); await Assert.ThrowsAsync<ApiException>(() => service.VerifyEsewaAsync(new(data), null, default)); await service.ProcessPendingReconciliationsAsync(default); Assert.Equal(0, gateways.Calls); }
    }
    [Fact]
    public async Task Watchdog_detects_artificial_overlap_without_weakening_production_constraint()
    {
        var s = await Seed(); var user = new User(s.Owner, s.Org); Guid original; await using (var db = Db(user)) original = (await Booking(db, user).CreateStaffBookingAsync(new(s.Court, "+9779800000031", "Watch", Start, Start.AddHours(1)), Guid.NewGuid().ToString(), null, default)).Id;
        await using var admin = Db(); Guid duplicate = Guid.Empty;
        await admin.Database.ExecuteSqlRawAsync("ALTER TABLE bookings DROP CONSTRAINT bookings_no_overlap");
        try { var a = await admin.Bookings.AsNoTracking().SingleAsync(x => x.Id == original); a.Id = Guid.NewGuid(); a.BookingCode = Guid.NewGuid().ToString(); duplicate = a.Id; admin.Bookings.Add(a); await admin.SaveChangesAsync(); await new PilotMonitoring(admin, new FixedClock(Now)).WatchAsync(default); Assert.Equal(1, (await admin.BookingWatches.SingleAsync(x => x.OrganizationId == s.Org)).OverlapCount); }
        finally { admin.ChangeTracker.Clear(); if (duplicate != Guid.Empty) await admin.Bookings.Where(x => x.Id == duplicate).ExecuteDeleteAsync(); await admin.Database.ExecuteSqlRawAsync("ALTER TABLE bookings ADD CONSTRAINT bookings_no_overlap EXCLUDE USING gist (court_id WITH =, tstzrange(start_at,end_at,'[)') WITH &&) WHERE (status NOT IN ('CANCELLED','EXPIRED'))"); }
    }
    [Fact]
    public async Task Feedback_and_screenshots_are_private_to_submitting_tenant_and_SuperAdmin()
    {
        var a = await Seed(); var b = await Seed(); Guid id; await using (var db = Db(new User(a.Owner, a.Org))) { var service = new FeedbackService(db, new User(a.Owner, a.Org), new FixedClock(Now)); id = await service.SubmitAsync(new("bug", "A private issue", "/admin/bookings?private=remove", Convert.ToBase64String(Png())), default); Assert.Single(await service.ListAsync(default)); Assert.Equal(Png(), await service.ScreenshotAsync(id, default)); }
        await using (var db = Db(new User(b.Owner, b.Org))) { var service = new FeedbackService(db, new User(b.Owner, b.Org), new FixedClock(Now)); Assert.Empty(await service.ListAsync(default)); await Assert.ThrowsAsync<ApiException>(() => service.ScreenshotAsync(id, default)); Assert.Null(await db.PilotFeedback.SingleOrDefaultAsync(x => x.Id == id)); }
        await using (var db = Db()) { Assert.DoesNotContain("private=", (await db.PilotFeedback.SingleAsync(x => x.Id == id)).PageUrl); Assert.NotEmpty(await new FeedbackService(db, new SuperAdminUser(), new FixedClock(Now)).ScreenshotAsync(id, default)); }
    }
    [Fact] public void Screenshot_rejects_active_content_corruption_and_oversize() { Assert.Throws<ArgumentException>(() => PilotScreenshot.Scan(Encoding.UTF8.GetBytes("<svg onload='alert(1)'/>"))); var corrupt = Png(); corrupt[30] ^= 1; Assert.Throws<ArgumentException>(() => PilotScreenshot.Scan(corrupt)); Assert.Throws<ArgumentException>(() => PilotScreenshot.Scan(new byte[PilotScreenshot.MaxBytes + 1])); }

    [Fact]
    public async Task Onboarding_saves_resumes_finishes_atomically_and_invites_are_single_use()
    {
        var seed = await Seed(); var user = new User(seed.Owner, seed.Org); var draft = new OnboardingDraft("Pilot new arena", "pilot-" + Guid.NewGuid().ToString("N"), seed.Location, "Street", [new("New court", 1200)], [new(5, new(6, 0), new(23, 0))], [new(0, "Peak", [5], new(17, 0), new(22, 0), 1600, 1)], Staff: [new("staff-" + Guid.NewGuid().ToString("N") + "@example.invalid", "Cashier")]);
        await using (var db = Db(user)) { await new OnboardingService(db, user, new FixedClock(Now), new()).SaveAsync(new(4, draft), default); }
        OnboardingResult result;
        await using (var db = Db(user)) { var service = new OnboardingService(db, user, new FixedClock(Now), new()); Assert.Equal(4, (await service.GetAsync(default)).Step); result = await service.FinishAsync(default); Assert.Single(result.Invites); Assert.Equal(result.VenueId, (await service.FinishAsync(default)).VenueId); Assert.Single(await db.Venues.Where(x => x.Id == result.VenueId).ToListAsync()); Assert.Equal(1, await db.CourtHours.CountAsync(x => db.Courts.Any(c => c.Id == x.CourtId && c.VenueId == result.VenueId))); Assert.Equal(1, await db.StaffInvites.CountAsync()); }
        var anonymous = new User(Guid.Empty, null, "", false); await using (var db = Db(anonymous)) { var service = new OnboardingService(db, anonymous, new FixedClock(Now), new()); await service.AcceptAsync(new(result.Invites[0].Token, "Staff member", "Correct-Horse-9027!"), default); await Assert.ThrowsAsync<ApiException>(() => service.AcceptAsync(new(result.Invites[0].Token, "Staff member", "Correct-Horse-9027!"), default)); }
        await using (var db = Db()) { var staff = await db.Users.SingleAsync(x => x.Email == result.Invites[0].Email); Assert.True(new PasswordHasher<AppUser>().VerifyHashedPassword(staff, staff.PasswordHash!, "Correct-Horse-9027!") != PasswordVerificationResult.Failed); Assert.Equal(result.VenueId, (await db.Memberships.SingleAsync(x => x.UserId == staff.Id)).VenueId); }
    }
    [Fact]
    public async Task New_pilot_records_enforce_tenant_read_and_write_filters()
    {
        var a = await Seed(); var b = await Seed(); await using (var db = Db()) { db.OnboardingStates.Add(new() { OrganizationId = b.Org, Draft = "{}", UpdatedAt = Now }); db.StaffInvites.Add(new() { OrganizationId = b.Org, TokenHash = Guid.NewGuid().ToString(), ExpiresAt = Now.AddDays(1) }); db.BookingWatches.Add(new() { OrganizationId = b.Org, CheckedAt = Now }); db.PilotErrors.Add(new() { OrganizationId = b.Org, CreatedAt = Now }); await db.SaveChangesAsync(); }
        await using (var db = Db(new User(a.Owner, a.Org))) { Assert.Empty(await db.OnboardingStates.ToListAsync()); Assert.Empty(await db.StaffInvites.ToListAsync()); Assert.Empty(await db.BookingWatches.ToListAsync()); Assert.Empty(await db.PilotErrors.ToListAsync()); db.PilotFeedback.Add(new() { OrganizationId = b.Org, Text = "Forbidden", CreatedAt = Now }); await Assert.ThrowsAsync<UnauthorizedAccessException>(() => db.SaveChangesAsync()); }
    }
    [Fact]
    public async Task Pilot_metrics_use_Nepal_day_and_venue_scope()
    {
        var seed = await Seed(); var user = new User(seed.Owner, seed.Org); await using (var db = Db(user)) { await Booking(db, user).CreateStaffBookingAsync(new(seed.Court, "+9779800000041", "Metric", Start, Start.AddHours(1)), Guid.NewGuid().ToString(), null, default); }
        // View a week that includes the future fixture booking, independent of the system clock.
        await using var admin = Db(); var report = (await new PilotMonitoring(admin, new FixedClock(Now.AddDays(2))).SummariesAsync(seed.Venue, default)).Single(); var day = Assert.Single(report.Days, x => x.Bookings == 1); Assert.Equal(new DateOnly(2026, 10, 16), day.Day); Assert.Equal(1, day.Staff); Assert.Equal(0, day.Online);
    }
    static byte[] Png() { using var output = new MemoryStream(); output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }); void Chunk(string type, byte[] payload) { Span<byte> length = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(length, payload.Length); output.Write(length); var bytes = Encoding.ASCII.GetBytes(type).Concat(payload).ToArray(); output.Write(bytes); uint crc = 0xffffffff; foreach (var b in bytes) { crc ^= b; for (int i = 0; i < 8; i++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320 : crc >> 1; } BinaryPrimitives.WriteUInt32BigEndian(length, ~crc); output.Write(length); } var header = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(header, 1); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), 1); header[8] = 8; header[9] = 6; Chunk("IHDR", header); using var compressed = new MemoryStream(); using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, true)) z.Write(new byte[] { 0, 0, 0, 0, 255 }); Chunk("IDAT", compressed.ToArray()); Chunk("IEND", []); return output.ToArray(); }
}
