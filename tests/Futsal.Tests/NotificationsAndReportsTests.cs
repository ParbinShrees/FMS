using Microsoft.Extensions.DependencyInjection;
using Futsal.Application.Abstractions;
using Futsal.Application.Notifications;
using Futsal.Application.Reports;
using Futsal.Domain.Bookings;
using Futsal.Domain.Identity;
using Futsal.Domain.Locations;
using Futsal.Infrastructure.Notifications;
using Futsal.Infrastructure.Persistence;
using Futsal.Infrastructure.Reports;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
namespace Futsal.Tests;

public sealed class NotificationsAndReportsTests
{
    [Theory]
    [InlineData("+9779800000000", true)]
    [InlineData("+9779700000000", true)]
    [InlineData("+9779600000000", true)]
    [InlineData("9800000000", false)]
    [InlineData("+919800000000", false)]
    [InlineData("+9779500000000", false)]
    [InlineData("+97798000000000", false)]
    public void Phone_validation(string phone, bool valid) => Assert.Equal(valid, SmsText.ValidPhone(phone));
    [Fact]
    public void Sms_segments_follow_GSM_extension_and_Unicode_limits()
    {
        Assert.Equal(1, SmsText.Count(new string('a', 160)).Segments); Assert.Equal(2, SmsText.Count(new string('a', 161)).Segments);
        Assert.Equal(1, SmsText.Count(new string('क', 70)).Segments); Assert.Equal(2, SmsText.Count(new string('क', 71)).Segments);
        Assert.Equal(160, SmsText.Count(new string('^', 80)).Units); Assert.Equal(2, SmsText.Count(new string('^', 81)).Segments);
        Assert.True(SmsText.Count("नेपाली").Unicode);
    }
    [Fact]
    public void Nepal_rendering_and_local_day_boundaries()
    {
        var instant = DateTimeOffset.Parse("2026-10-15T13:15:00Z"); Assert.Equal("7:00 PM", SmsText.NepalTime(instant));
        var range = ReportRules.UtcRange(new(2026, 10, 15), new(2026, 10, 15)); Assert.Equal(DateTimeOffset.Parse("2026-10-14T18:15:00Z"), range.Start); Assert.Equal(range.Start.AddDays(1), range.End);
        Assert.Equal("At 7:00 PM", SmsText.Render("At {time}", new Dictionary<string, string> { { "time", SmsText.NepalTime(instant) } }));
    }
    [Theory]
    [InlineData("=SUM(A1)")]
    [InlineData("+9779800000000")]
    [InlineData("-1")]
    [InlineData("@CMD")]
    [InlineData("  =HYPERLINK()")]
    [InlineData("\t=1")]
    public void Csv_neutralizes_formulas(string value) => Assert.StartsWith("\"'", ReportRules.CsvCell(value));
    [Fact] public void Csv_quotes_embedded_quotes_and_newlines() => Assert.Equal("\"a\"\"b\nc\"", ReportRules.CsvCell("a\"b\nc"));
    [Fact] public void Shared_BS_calendar_has_known_reference_and_roundtrips() { Assert.Equal("2080-01-01 BS", NepalDate.Bs(new(2023, 4, 14))); Assert.Equal(new DateOnly(2023, 4, 14), NepalDate.Ad("2080/01/01")); }
    [Fact]
    public void Failure_backoff_stops_after_five()
    {
        var now = DateTimeOffset.Parse("2026-10-15T00:00:00Z"); var n = new Futsal.Domain.Notifications.Notification();
        for (int i = 1; i <= 5; i++) { n.Fail(now); Assert.Equal(i, n.Attempts); Assert.Equal(now.AddMinutes(Math.Pow(2, i)), n.ScheduledFor); Assert.Equal(i == 5 ? "FAILED" : "QUEUED", n.Status); }
    }

    [Fact]
    public void Nepali_resources_resolve_and_pdf_supports_Devanagari()
    {
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("ne");
            using var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddLogging().AddLocalization(o => o.ResourcesPath = "Localization").BuildServiceProvider();
            var localizer = services.GetRequiredService<Microsoft.Extensions.Localization.IStringLocalizer<Futsal.Web.Milestone8>>(); Assert.False(localizer["Reports"].ResourceNotFound, localizer["Reports"].SearchedLocation + " | " + string.Join(",", typeof(Futsal.Web.Milestone8).Assembly.GetManifestResourceNames())); Assert.Equal("प्रतिवेदन", localizer["Reports"].Value);
            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community; var bytes = ReportExport.Pdf("प्रतिवेदन", [new() { Category = "income", Label = "नेपाली ग्राहक", Amount = 1500, Count = 1 }]); Assert.True(bytes.Length > 1000);
        }
        finally { System.Globalization.CultureInfo.CurrentUICulture = previous; }
    }
    [Fact]
    public void Pdf_export_is_a_real_document()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        var bytes = ReportExport.Pdf("FutsalPro", [new() { Category = "income", Label = "CASH", Amount = 100m, Count = 1 }]); Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }
}
public sealed class Milestone8PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:16-alpine").Build();
    public DbContextOptions<FutsalDbContext> Options { get; private set; } = null!;
    public async Task InitializeAsync() { await container.StartAsync(); Options = new DbContextOptionsBuilder<FutsalDbContext>().UseNpgsql(container.GetConnectionString(), FutsalDbContext.ConfigurePostgres).Options; await using var db = new FutsalDbContext(Options, new SuperAdminUser(), new FixedClock(DateTimeOffset.UtcNow)); await db.Database.MigrateAsync(); }
    public Task DisposeAsync() => container.DisposeAsync().AsTask();
}
public sealed class Milestone8PostgresTests(Milestone8PostgresFixture fixture) : IClassFixture<Milestone8PostgresFixture>, IAsyncLifetime
{
    public async Task InitializeAsync() { await using var db = Db(); await db.Database.ExecuteSqlRawAsync("TRUNCATE organizations,locations,notifications,notification_templates,notification_settings,sms_usage CASCADE"); }
    public Task DisposeAsync() => Task.CompletedTask;
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-15T11:15:00Z");
    private sealed class Clock : IClock { public DateTimeOffset UtcNow { get; set; } = Now; }
    private sealed class Sender : INotificationSender { public List<NotificationMessage> Messages { get; } = []; public bool Fail { get; set; } public Task SendAsync(NotificationMessage message, CancellationToken ct) { if (Fail) throw new HttpRequestException("sensitive provider text"); Messages.Add(message); return Task.CompletedTask; } }
    private sealed class OtherUser : ICurrentUser { public Guid? UserId => Guid.NewGuid(); public Guid? OrganizationId { get; } = Guid.NewGuid(); public string? Role => "Owner"; public bool IsAuthenticated => true; public bool IsSuperAdmin => false; }
    private FutsalDbContext Db(IClock? clock = null, ICurrentUser? user = null) => new(fixture.Options, user ?? new SuperAdminUser(), clock ?? new FixedClock(Now));
    private static async Task<(Organization Org, Venue Venue, Court Court, Customer Customer, Booking Booking)> Seed(FutsalDbContext db, DateTimeOffset? start = null)
    {
        var org = new Organization { Name = "M8", CreatedAt = Now }; var city = new Location { Type = LocationType.CITY, NameEn = "Kathmandu", Slug = Guid.NewGuid().ToString(), IsSeeded = true }; db.Organizations.Add(org); db.Locations.Add(city); await db.SaveChangesAsync();
        var venue = new Venue { OrganizationId = org.Id, LocationId = city.Id, CityId = city.Id, Name = "Arena", Slug = Guid.NewGuid().ToString() }; db.Venues.Add(venue);
        var court = new Court { OrganizationId = org.Id, VenueId = venue.Id, Name = "Court 1", PricePerSlot = 1500 }; db.Courts.Add(court);
        var customer = new Customer { OrganizationId = org.Id, FullName = "Customer", Phone = "+9779800000000", CreatedAt = Now }; db.Customers.Add(customer);
        var b = Booking.NewStaffConfirmed(); b.OrganizationId = org.Id; b.VenueId = venue.Id; b.CourtId = court.Id; b.CustomerId = customer.Id; b.BookingCode = Guid.NewGuid().ToString(); b.StartAt = start ?? Now.AddHours(2); b.EndAt = b.StartAt.AddHours(1); b.TotalAmount = 1500; b.CreatedAt = Now; b.UpdatedAt = Now; db.Bookings.Add(b); await db.SaveChangesAsync(); return (org, venue, court, customer, b);
    }
    [Fact]
    public async Task Confirmation_reminder_and_payment_are_unique_and_optout_respected()
    {
        var clock = new Clock(); await using var db = Db(clock); var s = await Seed(db); var sender = new Sender(); var q = new NotificationQueue(db, clock, new EphemeralDataProtectionProvider(), sender);
        await q.QueueEventsAndRemindersAsync(default); await q.QueueEventsAndRemindersAsync(default);
        Assert.Equal(2, await db.Notifications.CountAsync(x => x.BookingId == s.Booking.Id));
        await q.SendQueuedAsync(default); await q.SendQueuedAsync(default); Assert.Equal(2, sender.Messages.Count); Assert.Contains(sender.Messages, m => m.Text.Contains("7:00 PM"));
        s.Customer.OptOut = true; await db.SaveChangesAsync(); await q.QueueBookingAsync(s.Booking, "payment", "optout:" + Guid.NewGuid(), 50, default); Assert.False(await db.Notifications.AnyAsync(x => x.UniqueKey.StartsWith("optout:")));
    }
    [Fact]
    public async Task Reminder_waits_until_due_and_skips_cancelled_and_expired()
    {
        var clock = new Clock(); await using var db = Db(clock); var s = await Seed(db, Now.AddHours(3)); var sender = new Sender(); var q = new NotificationQueue(db, clock, new EphemeralDataProtectionProvider(), sender);
        await q.QueueEventsAndRemindersAsync(default); Assert.False(await db.Notifications.AnyAsync(n => n.BookingId == s.Booking.Id && n.Template == "reminder"));
        clock.UtcNow = Now.AddHours(1); await q.QueueEventsAndRemindersAsync(default); Assert.Single(await db.Notifications.Where(n => n.BookingId == s.Booking.Id && n.Template == "reminder").ToListAsync());
        s.Booking.Cancel(clock.UtcNow); await db.SaveChangesAsync(); await q.SendQueuedAsync(default); Assert.DoesNotContain(sender.Messages, m => m.Text.StartsWith("Reminder:"));
        var expired = Booking.NewHeld(); expired.OrganizationId = s.Org.Id; expired.VenueId = s.Venue.Id; expired.CourtId = s.Court.Id; expired.CustomerId = s.Customer.Id; expired.BookingCode = Guid.NewGuid().ToString(); expired.StartAt = Now.AddHours(4); expired.EndAt = Now.AddHours(5); expired.CreatedAt = Now; expired.UpdatedAt = Now; expired.Expire(); db.Bookings.Add(expired); await db.SaveChangesAsync(); await q.QueueEventsAndRemindersAsync(default); Assert.False(await db.Notifications.AnyAsync(n => n.BookingId == expired.Id));
    }
    [Fact]
    public async Task Failed_sends_backoff_then_stop_and_do_not_leak_error_text()
    {
        var clock = new Clock(); await using var db = Db(clock); var s = await Seed(db); var sender = new Sender { Fail = true }; var q = new NotificationQueue(db, clock, new EphemeralDataProtectionProvider(), sender); await q.QueueBookingAsync(s.Booking, "confirmation", "retry:" + Guid.NewGuid(), null, default);
        for (int i = 1; i <= 5; i++) { await q.SendQueuedAsync(default); var n = await db.Notifications.SingleAsync(x => x.BookingId == s.Booking.Id); Assert.Equal(i, n.Attempts); Assert.Equal("SMS_SEND_FAILED", n.Error); await q.SendQueuedAsync(default); Assert.Equal(i, n.Attempts); clock.UtcNow = n.ScheduledFor; }
        await q.SendQueuedAsync(default); Assert.Equal("FAILED", (await db.Notifications.SingleAsync(x => x.BookingId == s.Booking.Id)).Status);
    }
    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    public async Task Sms_caps_block_send_and_raise_alert(int daily, int monthly)
    {
        await using var db = Db(); var s = await Seed(db); var sender = new Sender(); var q = new NotificationQueue(db, new FixedClock(Now), new EphemeralDataProtectionProvider(), sender); var settings = await q.SettingsAsync(s.Venue.Id, s.Org.Id, default); settings.DailyLimit = daily; settings.MonthlyLimit = monthly; await db.SaveChangesAsync(); await q.QueueBookingAsync(s.Booking, "confirmation", "cap:" + Guid.NewGuid(), null, default); await q.SendQueuedAsync(default); Assert.Empty(sender.Messages); Assert.Equal("SMS_CAP_REACHED", settings.Alert);
    }
    [Fact]
    public async Task Optout_after_queue_is_checked_at_send_time()
    {
        await using var db = Db(); var s = await Seed(db); var sender = new Sender(); var q = new NotificationQueue(db, new FixedClock(Now), new EphemeralDataProtectionProvider(), sender); await q.QueueBookingAsync(s.Booking, "confirmation", "queued-optout:" + Guid.NewGuid(), null, default); s.Customer.OptOut = true; await db.SaveChangesAsync(); await q.SendQueuedAsync(default); Assert.Empty(sender.Messages);
    }
    [Fact]
    public async Task Report_totals_include_refunds_statuses_and_midnight_and_isolate_tenants()
    {
        await using var db = Db(); var s = await Seed(db, DateTimeOffset.Parse("2026-10-14T18:00:00Z")); // crosses Nepal midnight
        var b2 = Booking.NewStaffConfirmed(); b2.OrganizationId = s.Org.Id; b2.VenueId = s.Venue.Id; b2.CourtId = s.Court.Id; b2.CustomerId = s.Customer.Id; b2.BookingCode = Guid.NewGuid().ToString(); b2.StartAt = Now; b2.EndAt = Now.AddHours(1); b2.TotalAmount = 1000; b2.PaidAmount = 400; b2.CreatedAt = Now; b2.UpdatedAt = Now; b2.MarkNoShow(); db.Bookings.Add(b2);
        var b3 = Booking.NewStaffConfirmed(); b3.OrganizationId = s.Org.Id; b3.VenueId = s.Venue.Id; b3.CourtId = s.Court.Id; b3.CustomerId = s.Customer.Id; b3.BookingCode = Guid.NewGuid().ToString(); b3.StartAt = Now.AddHours(6); b3.EndAt = Now.AddHours(7); b3.TotalAmount = 1200; b3.CreatedAt = Now; b3.UpdatedAt = Now; b3.Cancel(Now); db.Bookings.Add(b3);
        var cashierOne = new AppUser { FullName = "Cashier 1", CreatedAt = Now }; var cashierTwo = new AppUser { FullName = "Cashier 2", CreatedAt = Now }; db.Users.AddRange(cashierOne, cashierTwo);
        db.Payments.AddRange(new Payment { OrganizationId = s.Org.Id, BookingId = s.Booking.Id, ReceivedBy = cashierOne.Id, Amount = 1000, Method = PaymentMethod.CASH, Status = PaymentStatus.COMPLETED, CreatedAt = Now, VerifiedAt = Now }, new Payment { OrganizationId = s.Org.Id, BookingId = s.Booking.Id, ReceivedBy = cashierOne.Id, Amount = -200, Method = PaymentMethod.CASH, Status = PaymentStatus.REFUNDED, CreatedAt = Now, VerifiedAt = Now }, new Payment { OrganizationId = s.Org.Id, BookingId = b2.Id, ReceivedBy = cashierTwo.Id, Amount = 400, Method = PaymentMethod.ESEWA, Status = PaymentStatus.COMPLETED, CreatedAt = Now, VerifiedAt = Now }, new Payment { OrganizationId = s.Org.Id, BookingId = b2.Id, Amount = 9000, Method = PaymentMethod.ESEWA, Status = PaymentStatus.PENDING, CreatedAt = Now });
        db.CourtHours.Add(new() { CourtId = s.Court.Id, Weekday = 4, OpenTime = new TimeOnly(16, 0), CloseTime = new TimeOnly(20, 0) });
        await db.SaveChangesAsync(); var reports = new ReportQueries(db); var day = await reports.ExecuteAsync("daily", s.Org.Id, s.Venue.Id, new(2026, 10, 15), new(2026, 10, 15), "Asia/Kathmandu", null, default); Assert.Equal(1200, day.Where(x => x.Category == "income").Sum(x => x.Amount)); Assert.Equal(1, day.Single(x => x.Category == "bookings" && x.Label == "NO_SHOW").Count); Assert.Equal(1, day.Single(x => x.Category == "bookings" && x.Label == "CANCELLED").Count); Assert.Equal(2, day.Where(x => x.Category == "bookings").Sum(x => x.Count));
        var month = await reports.ExecuteAsync("monthly", s.Org.Id, s.Venue.Id, new(2026, 10, 1), new(2026, 10, 31), "Asia/Kathmandu", null, default); Assert.Equal(1200, month.Where(x => x.Category == "income").Sum(x => x.Amount)); Assert.Equal(2, month.Single(x => x.Category == "bookings").Count); Assert.Equal(1250, month.Single(x => x.Category == "bookings").Value);
        var dues = await reports.ExecuteAsync("dues", s.Org.Id, s.Venue.Id, new(2026, 10, 1), new(2026, 10, 31), "Asia/Kathmandu", null, default); Assert.Equal(2100, dues.Sum(x => x.Amount));
        var customers = await reports.ExecuteAsync("customers", s.Org.Id, s.Venue.Id, new(2026, 10, 1), new(2026, 10, 31), "Asia/Kathmandu", null, default); Assert.Equal(1200, customers.Single().Amount); Assert.Equal(1, customers.Single().Value); Assert.Equal(2, customers.Single().Count);
        var utilization = await reports.ExecuteAsync("utilization", s.Org.Id, s.Venue.Id, new(2026, 10, 15), new(2026, 10, 15), "Asia/Kathmandu", null, default); Assert.Equal(4, utilization.Single(x => x.Category == "court").Count); Assert.Equal(25, utilization.Single(x => x.Category == "court").Value);
        var ownShift = await reports.ExecuteAsync("shift", s.Org.Id, s.Venue.Id, new(2026, 10, 15), new(2026, 10, 15), "Asia/Kathmandu", cashierOne.Id, default, Now.AddMinutes(-1), Now.AddMinutes(1)); Assert.Equal(800, ownShift.Sum(x => x.Amount)); Assert.Equal(2, ownShift.Sum(x => x.Count));
        var emptyEnd = await reports.ExecuteAsync("shift", s.Org.Id, s.Venue.Id, new(2026, 10, 15), new(2026, 10, 15), "Asia/Kathmandu", cashierOne.Id, default, Now.AddMinutes(-1), Now); Assert.Empty(emptyEnd);
        Assert.Empty(await reports.ExecuteAsync("shift", s.Org.Id, s.Venue.Id, new(2026, 10, 1), new(2026, 10, 31), "Asia/Kathmandu", Guid.NewGuid(), default));
        Assert.Empty(await reports.ExecuteAsync("daily", Guid.NewGuid(), s.Venue.Id, new(2026, 10, 15), new(2026, 10, 15), "Asia/Kathmandu", null, default).ContinueWith(t => t.Result.Where(x => x.Amount != 0 || x.Count != 0).ToList()));
    }

    [Fact]
    public async Task Concurrent_confirmation_enqueue_creates_exactly_one_row()
    {
        await using var db = Db(); var s = await Seed(db); var protection = new EphemeralDataProtectionProvider(); var q = new NotificationQueue(db, new FixedClock(Now), protection, new Sender()); await q.SettingsAsync(s.Venue.Id, s.Org.Id, default);
        await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ => { await using var concurrent = Db(); var queue = new NotificationQueue(concurrent, new FixedClock(Now), protection, new Sender()); await queue.QueueBookingAsync(s.Booking, "confirmation", $"booking:{s.Booking.Id}:confirmation", null, default); }));
        Assert.Single(await db.Notifications.Where(x => x.BookingId == s.Booking.Id).ToListAsync());
    }
    [Fact]
    public async Task Due_reminders_throttle_each_customer_for_three_days_and_track_usage()
    {
        var clock = new Clock(); await using var db = Db(clock); var s = await Seed(db); var sender = new Sender(); var q = new NotificationQueue(db, clock, new EphemeralDataProtectionProvider(), sender); var settings = await q.SettingsAsync(s.Venue.Id, s.Org.Id, default); settings.DueEnabled = true; await db.SaveChangesAsync();
        await q.DueRemindersAsync(default); await q.DueRemindersAsync(default); await q.SendQueuedAsync(default); Assert.Single(sender.Messages);
        var usage = await db.SmsUsage.SingleAsync(x => x.VenueId == s.Venue.Id); Assert.Equal(1, usage.Segments); Assert.Equal(1, usage.DeliveredSegments);
        clock.UtcNow = Now.AddDays(2); await q.DueRemindersAsync(default); Assert.Single(await db.Notifications.Where(x => x.Template == "due").ToListAsync());
        clock.UtcNow = Now.AddDays(3); await q.DueRemindersAsync(default); Assert.Equal(2, await db.Notifications.CountAsync(x => x.Template == "due"));
    }
    [Fact]
    public async Task Venue_otp_uses_selected_language_and_encrypted_queue_and_cap()
    {
        await using var db = Db(); var s = await Seed(db); var sender = new Sender(); var q = new NotificationQueue(db, new FixedClock(Now), new EphemeralDataProtectionProvider(), sender);
        await q.QueueOtpAsync(s.Customer.Phone, "123456", default, s.Venue.Id, "ne"); var row = await db.Notifications.SingleAsync(x => x.Template == "otp"); Assert.DoesNotContain("123456", row.Payload); Assert.Equal(s.Venue.Id, row.VenueId); Assert.True(row.Segments > 0);
        await q.SendQueuedAsync(default); Assert.Single(sender.Messages); Assert.Contains("123456", sender.Messages[0].Text); Assert.Equal("", row.Payload);
    }
    [Fact]
    public async Task Cashier_can_only_read_own_shift_and_other_tenant_cannot_open_reports_or_logs()
    {
        await using var db = Db(); var s = await Seed(db); var user = new AppUser { FullName = "Cashier", CreatedAt = Now }; db.Users.Add(user); db.Memberships.Add(new() { OrganizationId = s.Org.Id, UserId = user.Id, Role = OrganizationRole.CASHIER }); await db.SaveChangesAsync();
        var current = new TenantUser(user.Id, s.Org.Id, "Cashier"); await using var tenantDb = Db(user: current); var controller = new Futsal.Api.Controllers.ReportsController(tenantDb, current, new ReportQueries(tenantDb), new FixedClock(Now));
        var exception = await Assert.ThrowsAsync<Futsal.Api.Identity.ApiException>(() => controller.Get("daily", s.Venue.Id, new(2026, 10, 15), new(2026, 10, 15), null, null, default)); Assert.Equal(403, exception.StatusCode);
        var denied = await Assert.ThrowsAsync<Futsal.Api.Identity.ApiException>(() => controller.Get("shift", s.Venue.Id, new(2026, 10, 15), new(2026, 10, 15), Guid.NewGuid(), null, default)); Assert.Equal(403, denied.StatusCode);
        Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(await controller.Get("shift", s.Venue.Id, new(2026, 10, 15), new(2026, 10, 15), null, null, default));
        var other = new TenantUser(user.Id, Guid.NewGuid(), "Owner"); await using var otherDb = Db(user: other); var otherReports = new Futsal.Api.Controllers.ReportsController(otherDb, other, new ReportQueries(otherDb), new FixedClock(Now)); var hidden = await Assert.ThrowsAsync<Futsal.Api.Identity.ApiException>(() => otherReports.Get("daily", s.Venue.Id, new(2026, 10, 15), new(2026, 10, 15), null, null, default)); Assert.Equal(404, hidden.StatusCode);
        var queue = new NotificationQueue(otherDb, new FixedClock(Now), new EphemeralDataProtectionProvider(), new Sender()); var logController = new Futsal.Api.Controllers.NotificationsController(otherDb, other, queue, new FixedClock(Now)); await Assert.ThrowsAsync<Futsal.Api.Identity.ApiException>(() => logController.Logs(s.Venue.Id, ct: default));
    }
    private sealed class TenantUser(Guid user, Guid org, string role) : ICurrentUser { public Guid? UserId => user; public Guid? OrganizationId => org; public string? Role => role; public bool IsAuthenticated => true; public bool IsSuperAdmin => false; }
    [Fact]
    public async Task Notification_query_filter_and_write_guard_isolate_organizations()
    {
        await using var db = Db(); var s = await Seed(db); var q = new NotificationQueue(db, new FixedClock(Now), new EphemeralDataProtectionProvider(), new Sender()); await q.QueueBookingAsync(s.Booking, "confirmation", "isolation:" + Guid.NewGuid(), null, default);
        await using var other = Db(user: new OtherUser()); Assert.Empty(await other.Notifications.ToListAsync()); Assert.Empty(await other.NotificationSettings.ToListAsync());
        other.Notifications.Add(new() { OrganizationId = s.Org.Id, UniqueKey = Guid.NewGuid().ToString() }); await Assert.ThrowsAsync<UnauthorizedAccessException>(() => other.SaveChangesAsync());
    }
}

