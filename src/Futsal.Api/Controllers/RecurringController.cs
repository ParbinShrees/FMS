using Futsal.Api.Scheduling;
using Futsal.Application.Abstractions;
using Futsal.Application.Scheduling;
using Futsal.Contracts;
using Futsal.Infrastructure.Persistence;
using Futsal.Api.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace Futsal.Api.Controllers;
[ApiController,Route("api/v1/staff/recurring"),Authorize(Policy="StaffAccess")]
public sealed class RecurringController(RecurringService service,FutsalDbContext db,IClock clock,ICurrentUser current):ControllerBase
{
 [HttpGet]public async Task<IActionResult> List(Guid? venueId,string? status,CancellationToken ct)=>Ok(await service.ListAsync(venueId,status,ct));
 [HttpGet("options")]public async Task<IActionResult> Options(Guid venueId,CancellationToken ct){var venue=await db.Venues.SingleOrDefaultAsync(x=>x.Id==venueId,ct)??throw new ApiException(404,"NOT_FOUND","NOT_FOUND");return Ok(new SchedulingOptions(await db.Courts.Where(x=>x.VenueId==venue.Id&&x.IsActive).OrderBy(x=>x.Name).Select(x=>new SchedulingCourt(x.Id,x.Name,x.SlotMinutes)).ToListAsync(ct),await db.Customers.OrderBy(x=>x.FullName).Select(x=>new SchedulingCustomer(x.Id,x.FullName,x.Phone)).Take(1000).ToListAsync(ct)));}
 [HttpPost("preview")]public async Task<IActionResult> Preview(SeriesWrite input,CancellationToken ct)=>Ok(await service.PreviewAsync(input,ct));
 [HttpPost]public async Task<IActionResult> Create(SeriesCreate input,[FromHeader(Name="Idempotency-Key")]string? key,CancellationToken ct){BookingsController.RequireKey(key);return Ok(await service.CreateAsync(input,key!,ct));}
 [HttpGet("alerts")]public async Task<IActionResult> Alerts(CancellationToken ct)=>Ok(await db.SeriesConflicts.Where(x=>x.ResolvedAt==null).Select(x=>new SchedulingAlert(x.SeriesId,x.Date,x.Reason)).Take(200).ToListAsync(ct));
 [HttpGet("{id:guid}")]public async Task<IActionResult> Detail(Guid id,CancellationToken ct)=>Ok(await service.DetailAsync(id,ct));
 [HttpPost("{id:guid}/state/{action}")]public async Task<IActionResult> State(Guid id,string action,SeriesFromDate input,[FromHeader(Name="Idempotency-Key")]string? key,CancellationToken ct){BookingsController.RequireKey(key);if(action is not ("pause" or "resume" or "end"))return NotFound();await service.StateAsync(id,action,input,key!,ct);return NoContent();}
 [HttpDelete("{id:guid}"),Authorize(Policy="CanManageStaff")]public async Task<IActionResult> Delete(Guid id,SeriesFromDate input,[FromHeader(Name="Idempotency-Key")]string? key,CancellationToken ct){BookingsController.RequireKey(key);await service.StateAsync(id,"delete",input,key!,ct);return NoContent();}
 [HttpPost("{id:guid}/dates/{action}")]public async Task<IActionResult> Date(Guid id,string action,SeriesDateAction input,[FromHeader(Name="Idempotency-Key")]string? key,CancellationToken ct){BookingsController.RequireKey(key);await service.OccurrenceAsync(id,action,input,key!,ct);return NoContent();}
 [HttpPost("{id:guid}/change")]public async Task<IActionResult> Change(Guid id,SeriesChange input,[FromHeader(Name="Idempotency-Key")]string? key,CancellationToken ct){BookingsController.RequireKey(key);return Ok(await service.ChangeAsync(id,input,key!,ct));}
 [HttpPost("{id:guid}/prepay")]public async Task<IActionResult> Prepay(Guid id,SeriesPrepay input,[FromHeader(Name="Idempotency-Key")]string? key,CancellationToken ct){BookingsController.RequireKey(key);return Ok(await service.PrepayAsync(id,input,key!,ct));}
 [HttpGet("venues/{id:guid}/settings")]public async Task<IActionResult> Settings(Guid id,CancellationToken ct){var v=await db.Venues.SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new ApiException(404,"NOT_FOUND","NOT_FOUND");return Ok(new SchedulingSettings(v.RecurringHorizonWeeks,v.WaitlistOfferMinutes,v.WaitlistNoShowThreshold,v.WeeklySeriesSummary));}
 [HttpPut("venues/{id:guid}/settings"),Authorize(Policy="CanManageStaff")]public async Task<IActionResult> Settings(Guid id,SchedulingSettings input,CancellationToken ct){if(input.HorizonWeeks is <1 or >26||input.OfferMinutes is <1 or >60||input.NoShowThreshold is <0 or >100)throw new ApiException(400,"VALIDATION_FAILED","VALIDATION_FAILED");var v=await db.Venues.SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new ApiException(404,"NOT_FOUND","NOT_FOUND");v.RecurringHorizonWeeks=input.HorizonWeeks;v.WaitlistOfferMinutes=input.OfferMinutes;v.WaitlistNoShowThreshold=input.NoShowThreshold;v.WeeklySeriesSummary=input.WeeklySummary;db.AuditLogs.Add(new(){OrganizationId=v.OrganizationId,UserId=current.UserId,Action="SCHEDULING_SETTINGS_CHANGED",EntityType="Venue",EntityId=id,After=System.Text.Json.JsonSerializer.Serialize(input),CreatedAt=clock.UtcNow});await db.SaveChangesAsync(ct);return NoContent();}
}
