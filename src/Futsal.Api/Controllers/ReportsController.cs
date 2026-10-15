using Futsal.Api.Identity;
using Futsal.Application.Abstractions;
using Futsal.Contracts;
using Futsal.Infrastructure.Persistence;
using Futsal.Infrastructure.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace Futsal.Api.Controllers;

[ApiController, Route("api/v1/reports"), Authorize(Policy = "StaffAccess")]
public sealed class ReportsController(FutsalDbContext db, ICurrentUser current, ReportQueries reports, IClock clock) : ControllerBase
{
    [HttpGet("{kind}")]
    public async Task<IActionResult> Get(string kind, Guid venueId, DateOnly from, DateOnly to, Guid? cashierId, string? format, CancellationToken ct, DateTimeOffset? shiftStart = null, DateTimeOffset? shiftEnd = null)
    {
        if (!new[] { "daily", "monthly", "dues", "utilization", "customers", "shift" }.Contains(kind)) throw new ApiException(404, "NOT_FOUND", "Unknown report.");
        if (current.Role == "Cashier" && (kind != "shift" || cashierId != null && cashierId != current.UserId)) throw new ApiException(403, "FORBIDDEN", "Cashiers can only view their own shift.");
        if (kind == "shift" && (current.Role == "Cashier" || cashierId == null)) cashierId = current.UserId;
        var venue = await db.Venues.SingleOrDefaultAsync(x => x.Id == venueId && x.OrganizationId == current.OrganizationId, ct) ?? throw new ApiException(404, "NOT_FOUND", "Venue not found.");
        if (!await db.Memberships.AnyAsync(x => x.UserId == current.UserId && x.OrganizationId == venue.OrganizationId && (x.VenueId == null || x.VenueId == venueId), ct)) throw new ApiException(403, "FORBIDDEN", "Venue access denied.");
        if (to < from || to.DayNumber - from.DayNumber > 366) throw new ApiException(400, "VALIDATION_FAILED", "Select at most 367 days.");
        if (kind == "shift" && ((shiftStart == null) != (shiftEnd == null) || shiftStart != null && (shiftEnd <= shiftStart || shiftEnd - shiftStart > TimeSpan.FromDays(367)))) throw new ApiException(400, "VALIDATION_FAILED", "Invalid shift range.");
        var rows = await reports.ExecuteAsync(kind, venue.OrganizationId, venue.Id, from, to, venue.Timezone, kind == "shift" ? cashierId : null, ct, shiftStart, shiftEnd);
        if (kind == "monthly")
        {
            var income = rows.Where(x => x.Category == "income").Sum(x => x.Amount); var previous = rows.FirstOrDefault(x => x.Category == "comparison")?.Amount ?? 0;
            rows.Add(new() { Category = "comparison", Label = "current", Amount = income });
            rows.Add(new() { Category = "comparison", Label = "change", Amount = income - previous, Value = previous == 0 ? 0 : decimal.Round((income - previous) / Math.Abs(previous) * 100, 2), Detail = previous == 0 ? "NO_PREVIOUS_BASE" : null });
        }
        if (format is "csv" or "pdf")
        {
            db.AuditLogs.Add(new() { OrganizationId = venue.OrganizationId, UserId = current.UserId, Action = "REPORT_EXPORTED", EntityType = "Report", EntityId = venue.Id, CreatedAt = clock.UtcNow, After = System.Text.Json.JsonSerializer.Serialize(new { kind, from, to, format, cashierId }) }); await db.SaveChangesAsync(ct);
        }
        if (format == "csv") return File(ReportExport.Csv(rows), "text/csv; charset=utf-8", $"{kind}-{from:yyyyMMdd}.csv");
        if (format == "pdf") return File(ReportExport.Pdf($"FutsalPro {new System.Resources.ResourceManager("Futsal.Application.Reports.Labels", typeof(Futsal.Application.Reports.ReportRules).Assembly).GetString(kind, System.Globalization.CultureInfo.CurrentUICulture)} {from:yyyy-MM-dd} - {to:yyyy-MM-dd}", rows), "application/pdf", $"{kind}-{from:yyyyMMdd}.pdf");
        return Ok(new ReportResult(kind, from, to, rows.Select(x => new ReportRow(x.Category, x.Label, x.Amount, x.Count, x.Value, x.Detail)).ToList()));
    }
}
