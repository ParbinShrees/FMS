using Futsal.Api.Identity;
using Futsal.Application.Abstractions;
using Futsal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace Futsal.Api.Controllers;
[ApiController,Route("api/v1/admin/privacy/customers"),Authorize(Policy="OwnerOnly")]
public sealed class PrivacyController(FutsalDbContext db,ICurrentUser current,IClock clock):ControllerBase
{
 [HttpGet("{id:guid}/export")]
 public async Task<IActionResult> Export(Guid id,CancellationToken ct)
 {
  var c=await db.Customers.SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new ApiException(404,"NOT_FOUND","Customer not found.");
  var bookings=await db.Bookings.Where(x=>x.CustomerId==id).ToListAsync(ct);var ids=bookings.Select(x=>x.Id).ToArray();var payments=await db.Payments.Where(x=>x.BookingId!=null&&ids.Contains(x.BookingId.Value)).Select(x=>new{x.Id,x.BookingId,x.Amount,x.Method,x.Status,x.CreatedAt}).ToListAsync(ct);
  Audit(c.OrganizationId,id,"CUSTOMER_EXPORTED");await db.SaveChangesAsync(ct);return Ok(new{Customer=new{c.Id,c.FullName,c.Phone,c.Notes,c.OptOut},Bookings=bookings.Select(x=>new{x.Id,x.BookingCode,x.StartAt,x.EndAt,x.Status,x.TotalAmount,x.PaidAmount}),Payments=payments});
 }
 [HttpDelete("{id:guid}")]
 public async Task<IActionResult> Anonymize(Guid id,CancellationToken ct)
 {
  var c=await db.Customers.SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new ApiException(404,"NOT_FOUND","Customer not found.");
  var userId=c.UserId;c.FullName="Anonymized";c.Phone="deleted:"+c.Id;c.Notes=null;c.UserId=null;c.OptOut=true;
  var bookings=await db.Bookings.Where(x=>x.CustomerId==id).ToListAsync(ct);foreach(var b in bookings){b.Notes=null;b.CancelReason=null;}
  var messages=await db.Notifications.Where(x=>x.CustomerId==id).ToListAsync(ct);foreach(var n in messages){n.Recipient="";n.Payload="";n.Error=null;if(n.Status=="QUEUED")n.Status="FAILED";}
  // Account identity can be shared with other organizations: a venue Owner may only anonymize their own customer record.
  Audit(c.OrganizationId,id,"CUSTOMER_ANONYMIZED");await db.SaveChangesAsync(ct);return NoContent();
 }
 private void Audit(Guid org,Guid id,string action)=>db.AuditLogs.Add(new(){OrganizationId=org,UserId=current.UserId,Action=action,EntityType="Customer",EntityId=id,CreatedAt=clock.UtcNow});
}
