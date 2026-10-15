using Futsal.Api.Pilot;
using Futsal.Api.Identity;
using Futsal.Contracts;
using Futsal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
namespace Futsal.Api.Controllers;

[ApiController, Route("api/v1/pilot"), Authorize(Policy = "StaffAccess")]
public sealed class PilotController(OnboardingService onboarding, PilotImportService imports, FeedbackService feedback, PilotMonitoring monitoring, DemoService demos, FutsalDbContext db) : ControllerBase
{
    [HttpGet("context")] public async Task<IActionResult> Context(CancellationToken ct) => Ok(new { IsDemo = await db.Organizations.AnyAsync(x => x.IsDemo, ct) });
    [HttpGet("onboarding/locations"), Authorize(Policy = "OwnerOnly")] public async Task<IActionResult> Locations(CancellationToken ct) => Ok(await db.Locations.Where(x => x.IsActive && (x.Type == Futsal.Domain.Locations.LocationType.CITY || x.Type == Futsal.Domain.Locations.LocationType.AREA)).Select(x => new { x.Id, x.ParentId, Type = x.Type.ToString(), x.NameEn, x.NameNe, x.Slug, x.Path, x.Latitude, x.Longitude }).ToListAsync(ct));
    [HttpGet("onboarding"), Authorize(Policy = "OwnerOnly")] public async Task<IActionResult> Onboarding(CancellationToken ct) => Ok(await onboarding.GetAsync(ct));
    [HttpPut("onboarding"), Authorize(Policy = "OwnerOnly")] public async Task<IActionResult> Save(OnboardingSave input, CancellationToken ct) { await onboarding.SaveAsync(input, ct); return NoContent(); }
    [HttpPost("onboarding/finish"), Authorize(Policy = "OwnerOnly")] public async Task<IActionResult> Finish(CancellationToken ct) => Ok(await onboarding.FinishAsync(ct));
    [HttpPost("imports/{kind}"), RequestSizeLimit(300000), Authorize(Policy = "CanManageStaff")] public async Task<IActionResult> Import(string kind, ImportRequest input, CancellationToken ct) { if (kind is not ("customers" or "bookings")) return NotFound(); return Ok(await imports.RunAsync(input, kind == "bookings", ct)); }
    [HttpGet("imports/{kind}/template"), Authorize(Policy = "CanManageStaff")] public IActionResult Template(string kind, [FromQuery] string language = "en") { if (kind is not ("customers" or "bookings")) return NotFound(); var header = kind == "customers" ? (language == "ne" ? "नाम,फोन" : "name,phone") : (language == "ne" ? "कोर्ट,सुरु,अन्त्य,ग्राहक फोन,तिरेको रकम" : "court,start,end,customer phone,amount paid"); return File(System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(header + "\r\n")).ToArray(), "text/csv", kind + "-" + language + ".csv"); }
    [HttpPost("feedback"), RequestSizeLimit(300000)] public async Task<IActionResult> Submit(FeedbackWrite input, CancellationToken ct) => Ok(new { Id = await feedback.SubmitAsync(input, ct) });
    [HttpGet("feedback")] public async Task<IActionResult> Feedback(CancellationToken ct) => Ok(await feedback.ListAsync(ct));
    [HttpGet("feedback/{id:guid}/screenshot"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)] public async Task<IActionResult> Screenshot(Guid id, CancellationToken ct) => Ok(new { Base64 = Convert.ToBase64String(await feedback.ScreenshotAsync(id, ct)) });
    [HttpPut("feedback/{id:guid}/status"), Authorize(Policy = "SuperAdmin")] public async Task<IActionResult> Status(Guid id, [FromBody] string status, CancellationToken ct) { if (status is not ("NEW" or "SEEN" or "DONE")) throw new ApiException(400, "VALIDATION_FAILED", "VALIDATION_FAILED"); var row = await db.PilotFeedback.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ApiException(404, "NOT_FOUND", "NOT_FOUND"); row.Status = status; await db.SaveChangesAsync(ct); return NoContent(); }
    [HttpGet("monitoring/venues"), Authorize(Policy = "SuperAdmin")] public async Task<IActionResult> MonitoringVenues(CancellationToken ct) => Ok(await db.Venues.Select(x => new { x.Id, x.OrganizationId, x.Name }).ToListAsync(ct));
    [HttpGet("monitoring"), Authorize(Policy = "SuperAdmin")] public async Task<IActionResult> Monitoring(CancellationToken ct) => Ok(await monitoring.SummariesAsync(null, ct));
    [HttpGet("weekly/{venueId:guid}"), Authorize(Policy = "SuperAdmin")] public async Task<IActionResult> Weekly(Guid venueId, CancellationToken ct) => Ok(await monitoring.SummariesAsync(venueId, ct));
    [HttpPost("demo"), Authorize(Policy = "SuperAdmin")] public async Task<IActionResult> Demo(CancellationToken ct) { var id = await demos.CreateAsync(ct); return Ok(await demos.AccessAsync(id, ct)); }
    [HttpPost("demo/{id:guid}/reset"), Authorize(Policy = "SuperAdmin")] public async Task<IActionResult> Reset(Guid id, CancellationToken ct) { await demos.ResetAsync(id, ct); return NoContent(); }
}
[ApiController, Route("api/v1/auth/invites"), AllowAnonymous, EnableRateLimiting("auth")]
public sealed class PilotInviteController(OnboardingService onboarding) : ControllerBase
{
    [HttpPost("accept")] public async Task<IActionResult> Accept(InviteAccept input, CancellationToken ct) { await onboarding.AcceptAsync(input, ct); return NoContent(); }
}
