using Microsoft.AspNetCore.RateLimiting;
using Futsal.Api.Contracts;
using Futsal.Api.Identity;
using Futsal.Application.Abstractions;
using Futsal.Domain.Bookings;
using Futsal.Domain.Identity;
using Futsal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Futsal.Api.Controllers;

[EnableRateLimiting("payments")]
[ApiController, Route("api/v1/staff/payments"), Authorize(Policy = "StaffAccess")]
public sealed class StaffPaymentsController(
    FutsalDbContext db,
    ICurrentUser current,
    IClock clock) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpGet("needs-refund")]
    public async Task<ActionResult<List<NeedsRefundPaymentDto>>> GetNeedsRefund(CancellationToken ct)
    {
        var orgId = current.OrganizationId ?? throw new UnauthorizedAccessException();

        var query = from p in db.Payments
                    where p.OrganizationId == orgId && p.Status == PaymentStatus.NEEDS_REFUND && p.Amount+(db.Payments.Where(r=>r.ReversalOf==p.Id).Sum(r=>(decimal?)r.Amount)??0)>0
                    join b in db.Bookings on p.BookingId equals b.Id into bj
                    from b in bj.DefaultIfEmpty()
                    join c in db.Customers on b.CustomerId equals c.Id into cj
                    from c in cj.DefaultIfEmpty()
                    orderby p.CreatedAt descending
                    select new NeedsRefundPaymentDto(
                        p.Id,
                        p.OrganizationId,
                        p.BookingId,
                        b != null ? b.BookingCode : null,
                        c != null ? c.FullName : null,
                        c != null ? c.Phone : null,
                        p.Amount,
                        p.Method.ToString(),
                        p.GatewayTxnUuid,
                        p.GatewayRef,
                        p.CreatedAt,
                        p.VerifiedAt);

        return Ok(await query.ToListAsync(ct));
    }

    [HttpPost("{id:guid}/refund"), Authorize(Policy = "CanRefund")]
    public async Task<IActionResult> RecordRefund(
        Guid id,
        [FromBody] RecordPaymentRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? key,
        CancellationToken ct)
    {
        BookingsController.RequireKey(key);
        var userId = current.UserId ?? throw new UnauthorizedAccessException();
        var orgId = current.OrganizationId ?? throw new UnauthorizedAccessException();

        var endpoint=$"POST:/api/v1/staff/payments/{id}/refund";
        var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request,JsonOptions))));
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"idempotency:"+userId+":"+key},0))",ct);
        var replay=await db.IdempotencyKeys.SingleOrDefaultAsync(x=>x.Key==key&&x.UserId==userId&&x.Endpoint==endpoint,ct);
        if(replay!=null){if(replay.RequestHash!=hash)throw new ApiException(409,"IDEMPOTENCY_CONFLICT","Idempotency key already used.");await tx.CommitAsync(ct);return NoContent();}
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"refund:"+id},0))",ct);
        var originalPayment=await db.Payments.SingleOrDefaultAsync(p=>p.Id==id&&p.OrganizationId==orgId,ct)??throw new ApiException(404,"NOT_FOUND","Payment was not found.");
        if(request.Amount is < -9999999999.99m or > 9999999999.99m||!Enum.IsDefined(request.Method))throw new ApiException(400,"VALIDATION_FAILED","Invalid refund amount or method.");
        var amount=Math.Abs(request.Amount);
        var already=-await db.Payments.Where(x=>x.ReversalOf==id).SumAsync(x=>x.Amount,ct);
        if(originalPayment.Status is not (PaymentStatus.COMPLETED or PaymentStatus.NEEDS_REFUND)||amount<=0||decimal.Round(amount,2)!=amount||amount>originalPayment.Amount-already)throw new ApiException(400,"VALIDATION_FAILED","Refund exceeds the remaining received amount.");
        var refund=new Payment{OrganizationId=orgId,BookingId=originalPayment.BookingId,ReversalOf=id,Method=request.Method,Status=PaymentStatus.REFUNDED,Amount=-amount,GatewayRef=request.Reference,ReceivedBy=userId,CreatedAt=clock.UtcNow,VerifiedAt=clock.UtcNow};
        if(originalPayment.Status==PaymentStatus.COMPLETED&&originalPayment.BookingId is Guid bookingId){var booking=await db.Bookings.SingleAsync(x=>x.Id==bookingId,ct);if(booking.PaidAmount<amount)throw new ApiException(409,"VALIDATION_FAILED","Booking balance changed. Reload before refunding.");booking.PaidAmount-=amount;}
        db.Payments.Add(refund);
        db.IdempotencyKeys.Add(new(){Key=key!,UserId=userId,Endpoint=endpoint,RequestHash=hash,ResponseStatus=204,ResponseBody=JsonSerializer.Serialize(new{refund.Id},JsonOptions),CreatedAt=clock.UtcNow});
        db.AuditLogs.Add(new(){OrganizationId=orgId,UserId=userId,Action="PAYMENT_REFUNDED",EntityType="Payment",EntityId=refund.Id,After=JsonSerializer.Serialize(new{originalPaymentId=id,Amount=-amount},JsonOptions),CreatedAt=clock.UtcNow});
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);

        return NoContent();
    }
}
