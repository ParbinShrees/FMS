using System.Globalization;
using System.Resources;
using Futsal.Application.Abstractions;
using Futsal.Application.Notifications;
using Futsal.Domain.Bookings;
using Futsal.Domain.Notifications;
using Futsal.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
namespace Futsal.Infrastructure.Notifications;

public sealed class NotificationQueue(FutsalDbContext db, IClock clock, IDataProtectionProvider protection, INotificationSender sender)
{
    private readonly IDataProtector protector = protection.CreateProtector("FutsalPro.NotificationPayload.v1");
    public static readonly string[] Names = ["confirmation", "payment", "reminder", "cancellation", "due", "otp"];
    private static readonly ResourceManager Resources = new("Futsal.Application.Notifications.Templates", typeof(SmsText).Assembly);
    public static string DefaultText(string name, string language) => Resources.GetString(name, CultureInfo.GetCultureInfo(language)) ?? throw new ArgumentException("Unknown template");
    public async Task<NotificationSettings> SettingsAsync(Guid venueId, Guid orgId, CancellationToken ct)
    {
        var settings = await db.NotificationSettings.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.VenueId == venueId && x.OrganizationId == orgId, ct);
        if (settings != null) return settings;
        if (db.Database.IsRelational())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO notification_settings(id,organization_id,venue_id,enabled_at,confirmation_enabled,payment_enabled,cancellation_enabled,reminder_enabled,due_enabled,include_bs_date,reminder_hours,daily_limit,monthly_limit,language) VALUES ({Guid.NewGuid()},{orgId},{venueId},{clock.UtcNow},true,true,true,true,false,false,2,100,2000,'en') ON CONFLICT(venue_id) DO NOTHING", ct);
            foreach (var language in new[] { "en", "ne" }) foreach (var name in Names)
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO notification_templates(id,organization_id,venue_id,name,language,text) VALUES ({Guid.NewGuid()},{orgId},{venueId},{name},{language},{DefaultText(name, language)}) ON CONFLICT(venue_id,name,language) DO NOTHING", ct);
            return await db.NotificationSettings.IgnoreQueryFilters().SingleAsync(x => x.VenueId == venueId && x.OrganizationId == orgId, ct);
        }
        settings = new() { VenueId = venueId, OrganizationId = orgId, EnabledAt = clock.UtcNow }; db.NotificationSettings.Add(settings);
        foreach (var language in new[] { "en", "ne" }) foreach (var name in Names)
            db.NotificationTemplates.Add(new() { OrganizationId = orgId, VenueId = venueId, Name = name, Language = language, Text = DefaultText(name, language) });
        await db.SaveChangesAsync(ct); return settings;
    }
    private async Task QueueAsync(Guid org, Guid? venue, Guid? booking, Guid? customer, string phone, string template, string text, string key, CancellationToken ct)
    {

        if (await db.Notifications.IgnoreQueryFilters().AnyAsync(n => n.UniqueKey == key, ct)) return;
        if (db.Database.IsRelational())
        {
            var status = SmsText.ValidPhone(phone) ? "QUEUED" : "FAILED"; var error = SmsText.ValidPhone(phone) ? null : "INVALID_PHONE";
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO notifications(id,organization_id,venue_id,booking_id,customer_id,channel,template,unique_key,recipient,payload,status,attempts,segments,created_at,scheduled_for,error) VALUES ({Guid.NewGuid()},{org},{venue},{booking},{customer},'SMS',{template},{key},{phone},{protector.Protect(text)},{status},0,{SmsText.Count(text).Segments},{clock.UtcNow},{clock.UtcNow},{error}) ON CONFLICT(unique_key) DO NOTHING", ct);
            return;
        }
        db.Notifications.Add(new() { Status = SmsText.ValidPhone(phone) ? "QUEUED" : "FAILED", Error = SmsText.ValidPhone(phone) ? null : "INVALID_PHONE", OrganizationId = org, VenueId = venue, BookingId = booking, CustomerId = customer, Recipient = phone, Template = template, Payload = protector.Protect(text), UniqueKey = key, Segments = SmsText.Count(text).Segments, CreatedAt = clock.UtcNow, ScheduledFor = clock.UtcNow });
        await db.SaveChangesAsync(ct);
    }
    public async Task QueueOtpAsync(string phone, string code, CancellationToken ct, Guid? venueId = null, string? language = null)
    {
        if (!SmsText.ValidPhone(phone)) throw new ArgumentException("Invalid Nepal mobile number");
        if (venueId is Guid id)
        {
            var venue = await db.Venues.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == id && x.IsPublic && x.IsActive, ct) ?? throw new ArgumentException("Venue unavailable");
            var customer = await db.Customers.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.OrganizationId == venue.OrganizationId && x.Phone == phone, ct);
            if (customer?.OptOut == true) return;
            var settings = await SettingsAsync(id, venue.OrganizationId, ct);
            language = language is "en" or "ne" ? language : settings.Language;
            var template = await db.NotificationTemplates.IgnoreQueryFilters().Where(x => x.VenueId == id && x.OrganizationId == venue.OrganizationId && x.Language == language && x.Name == "otp").Select(x => x.Text).SingleAsync(ct);
            await QueueAsync(venue.OrganizationId, id, null, customer?.Id, phone, "otp", SmsText.Render(template, new Dictionary<string, string> { { "code", code }, { "venue", venue.Name } }), "otp:" + Guid.NewGuid(), ct);
            return;
        }
        // Global login OTPs have no tenant; Guid.Empty is reserved for platform login usage.
        await QueueAsync(Guid.Empty, null, null, null, phone, "otp", SmsText.Render(DefaultText("otp", "en"), new Dictionary<string, string> { { "code", code } }), "otp:" + Guid.NewGuid(), ct);
    }
    public async Task QueueBookingAsync(Booking booking, string name, string unique, decimal? amount, CancellationToken ct)
    {
        if (booking.CustomerId == null) return;
        var customer = await db.Customers.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == booking.CustomerId && x.OrganizationId == booking.OrganizationId, ct);
        if (customer == null || customer.OptOut) return;
        var venue = await db.Venues.IgnoreQueryFilters().SingleAsync(x => x.Id == booking.VenueId && x.OrganizationId == booking.OrganizationId, ct);
        var settings = await SettingsAsync(venue.Id, venue.OrganizationId, ct);
        bool enabled = name switch { "confirmation" => settings.ConfirmationEnabled, "payment" => settings.PaymentEnabled, "reminder" => settings.ReminderEnabled, "cancellation" => settings.CancellationEnabled, "due" => settings.DueEnabled, _ => false };
        if (!enabled) return;
        var court = await db.Courts.IgnoreQueryFilters().Where(x => x.Id == booking.CourtId && x.OrganizationId == booking.OrganizationId).Select(x => x.Name).SingleAsync(ct);
        var local = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(booking.StartAt, venue.Timezone);
        var text = await db.NotificationTemplates.IgnoreQueryFilters().Where(x => x.VenueId == venue.Id && x.OrganizationId == venue.OrganizationId && x.Name == name && x.Language == settings.Language).Select(x => x.Text).SingleAsync(ct);
        text = SmsText.Render(text, new Dictionary<string, string> { { "venue", venue.Name }, { "court", court }, { "date", (local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + (settings.IncludeBsDate ? " / " + NepalDate.Bs(DateOnly.FromDateTime(local.DateTime)) : "")) }, { "time", local.ToString("h:mm tt", CultureInfo.InvariantCulture) }, { "code", booking.BookingCode }, { "amount", (amount ?? Math.Max(0, booking.TotalAmount - booking.PaidAmount)).ToString("0.00", CultureInfo.InvariantCulture) } });
        await QueueAsync(venue.OrganizationId, venue.Id, booking.Id, customer.Id, customer.Phone, name, text, unique, ct);
    }
    // Recovery scan also closes the gap if the process stops after a booking commits but before the event is handled.
    public async Task QueueEventsAndRemindersAsync(CancellationToken ct, bool reminders = true)
    {
        var now = clock.UtcNow;
        var bookings = await db.Bookings.IgnoreQueryFilters().Where(x => x.Status == BookingStatus.CONFIRMED || x.Status == BookingStatus.CANCELLED).OrderBy(x => x.Id).ToListAsync(ct);
        foreach (var b in bookings)
        {
            var eventSettings = await SettingsAsync(b.VenueId, b.OrganizationId, ct);
            if ((b.Status == BookingStatus.CONFIRMED ? b.CreatedAt : b.CancelledAt) >= eventSettings.EnabledAt) await QueueBookingAsync(b, b.Status == BookingStatus.CONFIRMED ? "confirmation" : "cancellation", $"booking:{b.Id}:" + (b.Status == BookingStatus.CONFIRMED ? "confirmation" : "cancellation"), null, ct);
            if (!reminders || b.Status != BookingStatus.CONFIRMED || b.StartAt <= now) continue;
            var settings = await SettingsAsync(b.VenueId, b.OrganizationId, ct);
            if (b.StartAt.AddHours(-settings.ReminderHours) <= now) await QueueBookingAsync(b, "reminder", $"booking:{b.Id}:reminder", null, ct);
        }
        var payments = await db.Payments.IgnoreQueryFilters().Where(x => x.Status == PaymentStatus.COMPLETED && x.Amount > 0 && x.BookingId != null).ToListAsync(ct);
        foreach (var p in payments)
        {
            var b = await db.Bookings.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == p.BookingId && x.OrganizationId == p.OrganizationId, ct);
            if (b != null && (p.VerifiedAt ?? p.CreatedAt) >= (await SettingsAsync(b.VenueId, b.OrganizationId, ct)).EnabledAt) await QueueBookingAsync(b, "payment", $"payment:{p.Id}", p.Amount, ct);
        }
    }
    public async Task DueRemindersAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var settings = await db.NotificationSettings.IgnoreQueryFilters().Where(x => x.DueEnabled).ToListAsync(ct);
        foreach (var s in settings)
        {
            var debtors = await db.Bookings.IgnoreQueryFilters().Where(x => x.VenueId == s.VenueId && x.OrganizationId == s.OrganizationId && (x.Status == BookingStatus.CONFIRMED || x.Status == BookingStatus.COMPLETED || x.Status == BookingStatus.NO_SHOW) && x.TotalAmount > x.PaidAmount && x.CustomerId != null).GroupBy(x => x.CustomerId).Select(g => new { Customer = g.Key, Amount = g.Sum(x => x.TotalAmount - x.PaidAmount) }).ToListAsync(ct);
            foreach (var d in debtors)
            {
                if (await db.Notifications.IgnoreQueryFilters().AnyAsync(x => x.OrganizationId == s.OrganizationId && x.CustomerId == d.Customer && x.Template == "due" && x.CreatedAt > now.AddDays(-3), ct)) continue;
                var b = await db.Bookings.IgnoreQueryFilters().Where(x => x.VenueId == s.VenueId && x.OrganizationId == s.OrganizationId && x.CustomerId == d.Customer && (x.Status == BookingStatus.CONFIRMED || x.Status == BookingStatus.COMPLETED || x.Status == BookingStatus.NO_SHOW) && x.TotalAmount > x.PaidAmount).OrderBy(x => x.StartAt).FirstAsync(ct);
                await QueueBookingAsync(b, "due", $"due:{s.VenueId}:{d.Customer}:{now.UtcDateTime:yyyyMMdd}", d.Amount, ct);
            }
        }
    }
    public async Task SendQueuedAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var pending = await db.Notifications.IgnoreQueryFilters().Where(x => x.Status == "QUEUED" && x.ScheduledFor <= now && x.Attempts < 5).OrderBy(x => x.ScheduledFor).Take(100).ToListAsync(ct);
        foreach (var n in pending)
        {
            if (n.CustomerId != null && await db.Customers.IgnoreQueryFilters().AnyAsync(x => x.Id == n.CustomerId && x.OrganizationId == n.OrganizationId && x.OptOut, ct)) { n.Status = "FAILED"; n.Error = "OPTED_OUT"; continue; }
            if (n.Template == "reminder" && !await db.Bookings.IgnoreQueryFilters().AnyAsync(x => x.Id == n.BookingId && x.OrganizationId == n.OrganizationId && x.Status == BookingStatus.CONFIRMED && x.StartAt > now, ct)) { n.Status = "FAILED"; n.Error = "BOOKING_INACTIVE"; continue; }
            if (!SmsText.ValidPhone(n.Recipient)) { n.Status = "FAILED"; n.Error = "INVALID_PHONE"; continue; }
            if (n.Template == "otp" && n.CreatedAt.AddMinutes(5) <= now) { n.Status = "FAILED"; n.Error = "OTP_EXPIRED"; continue; }
            SmsUsage? usage = null;
            if (n.VenueId is Guid venue)
            {
                var s = await SettingsAsync(venue, n.OrganizationId, ct);
                var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(now, "Asia/Kathmandu").DateTime);
                var month = new DateOnly(day.Year, day.Month, 1);
                usage = await db.SmsUsage.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.VenueId == venue && x.OrganizationId == n.OrganizationId && x.Day == day, ct);
                var monthly = await db.SmsUsage.IgnoreQueryFilters().Where(x => x.VenueId == venue && x.OrganizationId == n.OrganizationId && x.Day >= month && x.Day < month.AddMonths(1)).SumAsync(x => x.Segments, ct);
                if ((usage?.Segments ?? 0) + n.Segments > s.DailyLimit || monthly + n.Segments > s.MonthlyLimit)
                { n.Error = "SMS_CAP_REACHED"; n.ScheduledFor = now.AddHours(1); s.Alert = "SMS_CAP_REACHED"; await db.SaveChangesAsync(ct); continue; }
                if (usage == null) { usage = new() { OrganizationId = n.OrganizationId, VenueId = venue, Day = day }; db.SmsUsage.Add(usage); }
            }
            // Reserve the potential provider cost before the external request, including ambiguous failures.
            if (usage != null) usage.Segments += n.Segments;
            await db.SaveChangesAsync(ct);
            try
            {
                await sender.SendAsync(new(n.Recipient, protector.Unprotect(n.Payload), n.Id.ToString(), n.Template == "otp"), ct);
                n.Attempts++; n.Status = "SENT"; n.SentAt = now; n.Error = null;
                if (usage != null) usage.DeliveredSegments += n.Segments;
                if (n.Template == "otp") n.Payload = "";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { n.Fail(now); }
            await db.SaveChangesAsync(ct);
        }
        await db.SaveChangesAsync(ct);
    }
}
