using Futsal.Api.Identity;
using Futsal.Application.Abstractions;
using Futsal.Application.Notifications;
using Futsal.Contracts;
using Futsal.Infrastructure.Notifications;
using Futsal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace Futsal.Api.Controllers;

[ApiController, Route("api/v1/admin/venues/{venueId:guid}/notifications"), Authorize(Policy = "CanViewReports")]
public sealed class NotificationsController(FutsalDbContext db, ICurrentUser current, NotificationQueue queue, IClock clock) : ControllerBase
{
    private async Task<Guid> Access(Guid venueId, CancellationToken ct)
    {
        var venue = await db.Venues.SingleOrDefaultAsync(x => x.Id == venueId && x.OrganizationId == current.OrganizationId, ct) ?? throw new ApiException(404, "NOT_FOUND", "Venue not found.");
        if (!await db.Memberships.AnyAsync(x => x.UserId == current.UserId && x.OrganizationId == venue.OrganizationId && (x.VenueId == null || x.VenueId == venueId), ct)) throw new ApiException(403, "FORBIDDEN", "Venue access denied.");
        return venue.OrganizationId;
    }
    [HttpGet("settings")]
    public async Task<NotificationSettingsDto> Settings(Guid venueId, CancellationToken ct)
    {
        var org = await Access(venueId, ct); var s = await queue.SettingsAsync(venueId, org, ct);
        return new(s.ConfirmationEnabled, s.PaymentEnabled, s.CancellationEnabled, s.ReminderEnabled, s.DueEnabled, s.IncludeBsDate, s.ReminderHours, s.DailyLimit, s.MonthlyLimit, s.Language, s.Alert);
    }
    [HttpPut("settings")]
    public async Task<IActionResult> SaveSettings(Guid venueId, NotificationSettingsDto input, CancellationToken ct)
    {
        var org = await Access(venueId, ct);
        if (input.ReminderHours is < 1 or > 168 || input.DailyLimit < 0 || input.MonthlyLimit < 0 || !new[] { "en", "ne" }.Contains(input.Language)) throw new ApiException(400, "VALIDATION_FAILED", "Invalid notification settings.");
        var s = await queue.SettingsAsync(venueId, org, ct);
        s.ConfirmationEnabled = input.ConfirmationEnabled; s.PaymentEnabled = input.PaymentEnabled; s.CancellationEnabled = input.CancellationEnabled; s.ReminderEnabled = input.ReminderEnabled; s.DueEnabled = input.DueEnabled; s.IncludeBsDate = input.IncludeBsDate; s.ReminderHours = input.ReminderHours; s.DailyLimit = input.DailyLimit; s.MonthlyLimit = input.MonthlyLimit; s.Language = input.Language; s.Alert = null;
        await db.SaveChangesAsync(ct); return NoContent();
    }
    [HttpGet("templates")]
    public async Task<List<TemplateDto>> Templates(Guid venueId, CancellationToken ct)
    {
        await queue.SettingsAsync(venueId, await Access(venueId, ct), ct);
        return await db.NotificationTemplates.Where(x => x.VenueId == venueId).Select(x => new TemplateDto(x.Name, x.Language, x.Text)).ToListAsync(ct);
    }
    [HttpPut("templates")]
    public async Task<IActionResult> SaveTemplate(Guid venueId, TemplateDto input, CancellationToken ct)
    {
        await Access(venueId, ct);
        if (string.IsNullOrWhiteSpace(input.Text) || input.Text.Length > 1000) throw new ApiException(400, "VALIDATION_FAILED", "Template must contain 1 to 1000 characters.");
        var supported = new[] { "venue", "court", "date", "time", "code", "amount" };
        if (System.Text.RegularExpressions.Regex.Matches(input.Text, @"\{([^}]+)\}").Any(m => !supported.Contains(m.Groups[1].Value))) throw new ApiException(400, "VALIDATION_FAILED", "Unknown placeholder.");
        var t = await db.NotificationTemplates.SingleOrDefaultAsync(x => x.VenueId == venueId && x.Name == input.Name && x.Language == input.Language, ct) ?? throw new ApiException(404, "NOT_FOUND", "Template not found.");
        t.Text = input.Text; await db.SaveChangesAsync(ct); return NoContent();
    }
    [HttpPost("preview")]
    public async Task<SmsPreviewDto> Preview(Guid venueId, TemplateDto input, CancellationToken ct)
    {
        await Access(venueId, ct);
        var venue = await db.Venues.SingleAsync(x => x.Id == venueId, ct);
        var settings = await queue.SettingsAsync(venueId, venue.OrganizationId, ct);
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(clock.UtcNow, venue.Timezone).DateTime);
        var court = await db.Courts.Where(x => x.VenueId == venueId).OrderByDescending(x => x.Name.Length).Select(x => x.Name).FirstOrDefaultAsync(ct) ?? "";
        var text = SmsText.Render(input.Text, new Dictionary<string, string> { { "venue", venue.Name }, { "court", court }, { "date", date.ToString("yyyy-MM-dd") + (settings.IncludeBsDate ? " / " + NepalDate.Bs(date) : "") }, { "time", "7:00 PM" }, { "code", "FB-12345" }, { "amount", "1500.00" } });
        var count = SmsText.Count(text); return new(text, count.Characters, count.Units, count.Segments, count.Unicode);
    }
    [HttpGet("logs")]
    public async Task<List<NotificationLogDto>> Logs(Guid venueId, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        await Access(venueId, ct);
        var rows = await db.Notifications.Where(x => x.VenueId == venueId).OrderByDescending(x => x.CreatedAt).Skip((Math.Max(page, 1) - 1) * Math.Clamp(pageSize, 1, 100)).Take(Math.Clamp(pageSize, 1, 100)).Select(x => new { x.Id, x.Status, x.Recipient, x.Template, x.Attempts, x.Error, x.ScheduledFor }).ToListAsync(ct);
        return rows.Select(x => new NotificationLogDto(x.Id, x.Status, SmsText.Mask(x.Recipient), x.Template, x.Attempts, x.Error, x.ScheduledFor)).ToList();
    }
    [HttpPost("{id:guid}/resend")]
    public async Task<IActionResult> Resend(Guid venueId, Guid id, CancellationToken ct)
    {
        await Access(venueId, ct);
        var n = await db.Notifications.SingleOrDefaultAsync(x => x.VenueId == venueId && x.Id == id, ct) ?? throw new ApiException(404, "NOT_FOUND", "Notification not found.");
        if (n.Status != "FAILED" || n.Template == "otp") throw new ApiException(409, "VALIDATION_FAILED", "Only failed transactional messages may be retried.");
        n.Status = "QUEUED"; n.Attempts = 0; n.ScheduledFor = clock.UtcNow; n.Error = null; await db.SaveChangesAsync(ct); return NoContent();
    }
    [HttpGet("usage")]
    public async Task<IActionResult> Usage(Guid venueId, CancellationToken ct)
    {
        await Access(venueId, ct);
        return Ok(await db.SmsUsage.Where(x => x.VenueId == venueId).GroupBy(x => new { x.Day.Year, x.Day.Month }).Select(g => new { g.Key.Year, g.Key.Month, Segments = g.Sum(x => x.Segments), DeliveredSegments = g.Sum(x => x.DeliveredSegments) }).ToListAsync(ct));
    }
}
