using Futsal.Api.Identity;
using Futsal.Application.Abstractions;
using Futsal.Domain.Identity;
using Futsal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace Futsal.Api.Controllers;
public sealed record OrganizationLicenseWrite(string Name,string Plan,string Status,DateTimeOffset StartsAt,DateTimeOffset? EndsAt,int MaxCourts,int GraceDays,bool Suspended=false,decimal? ManualPayment=null,string? Reference=null);
[ApiController,Route("api/v1/platform/organizations"),Authorize(Policy="SuperAdmin")]
public sealed class SubscriptionsController(FutsalDbContext db,IClock clock,ICurrentUser current):ControllerBase
{
 [HttpGet]public async Task<IActionResult> List(CancellationToken ct)=>Ok(await db.Organizations.IgnoreQueryFilters().Select(o=>new{o.Id,o.Name,o.Status,Subscription=db.Subscriptions.IgnoreQueryFilters().Where(s=>s.OrganizationId==o.Id).Select(s=>new{s.Plan,s.Status,s.EndsAt,s.MaxCourts,s.GraceDays}).FirstOrDefault(),Courts=db.Courts.IgnoreQueryFilters().Count(c=>c.OrganizationId==o.Id&&c.IsActive),Bookings=db.Bookings.IgnoreQueryFilters().Count(b=>b.OrganizationId==o.Id),Sms=db.SmsUsage.IgnoreQueryFilters().Where(s=>s.OrganizationId==o.Id).Sum(s=>s.Segments)}).ToListAsync(ct));
 [HttpPost]public async Task<IActionResult> Create(OrganizationLicenseWrite input,CancellationToken ct){var org=new Organization{Name=input.Name,CreatedAt=clock.UtcNow};db.Organizations.Add(org);await Save(org,input,ct);return Ok(new{org.Id});}
 [HttpPut("{id:guid}")]public async Task<IActionResult> Update(Guid id,OrganizationLicenseWrite input,CancellationToken ct){var org=await db.Organizations.IgnoreQueryFilters().SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new ApiException(404,"NOT_FOUND","Organization not found.");await Save(org,input,ct);return NoContent();}
 private async Task Save(Organization org,OrganizationLicenseWrite input,CancellationToken ct)
 {
  if(string.IsNullOrWhiteSpace(input.Name)||input.Name.Length>160||input.ManualPayment<0)throw new ApiException(400,"VALIDATION_FAILED","Invalid organization.");
  var license=await db.Subscriptions.IgnoreQueryFilters().SingleOrDefaultAsync(x=>x.OrganizationId==org.Id,ct);
  if(license==null){license=new(){OrganizationId=org.Id};db.Subscriptions.Add(license);}
  license.Plan=input.Plan;license.Status=input.Status;license.StartsAt=input.StartsAt.ToUniversalTime();license.EndsAt=input.EndsAt?.ToUniversalTime();license.MaxCourts=input.MaxCourts;license.GraceDays=input.GraceDays;
  try{license.Validate();}catch(ArgumentException){throw new ApiException(400,"VALIDATION_FAILED","Invalid subscription.");}
  org.Name=input.Name.Trim();org.Status=input.Suspended?"SUSPENDED":"ACTIVE";
  db.AuditLogs.Add(new(){OrganizationId=org.Id,UserId=current.UserId,Action="SUBSCRIPTION_UPDATED",EntityType="Organization",EntityId=org.Id,After=System.Text.Json.JsonSerializer.Serialize(input),CreatedAt=clock.UtcNow});await db.SaveChangesAsync(ct);
 }
}
[ApiController,Route("api/v1/staff/subscription"),Authorize(Policy="StaffAccess")]
public sealed class SubscriptionStatusController(FutsalDbContext db,ICurrentUser current,IClock clock):ControllerBase
{
 [HttpGet]public async Task<IActionResult> Get(CancellationToken ct){var s=await db.Subscriptions.SingleOrDefaultAsync(x=>x.OrganizationId==current.OrganizationId,ct);var org=await db.Organizations.SingleOrDefaultAsync(x=>x.Id==current.OrganizationId,ct);return Ok(new{s?.Plan,s?.Status,s?.EndsAt,ReadOnly=org?.Status!="ACTIVE"||s==null||s.ReadOnly(clock.UtcNow),Warning=s?.EndsAt<=clock.UtcNow.AddDays(14)});}
}
