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
                    where p.OrganizationId == orgId && p.Status == PaymentStatus.NEEDS_REFUND
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

        var originalPayment = await db.Payments.SingleOrDefaultAsync(p => p.Id == id && p.OrganizationId == orgId, ct)
            ?? throw new ApiException(404, "NOT_FOUND", "Payment was not found.");

        if (originalPayment.Status == PaymentStatus.REFUNDED)
            throw new ApiException(400, "VALIDATION_FAILED", "This payment has already been refunded.");

        var refundAmount = -Math.Abs(request.Amount);

        var refundPayment = new Payment
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            BookingId = originalPayment.BookingId,
            Method = request.Method,
            Status = PaymentStatus.REFUNDED,
            Amount = refundAmount,
            GatewayRef = request.Reference,
            ReceivedBy = userId,
            CreatedAt = clock.UtcNow,
            VerifiedAt = clock.UtcNow
        };

        if (originalPayment.Status == PaymentStatus.NEEDS_REFUND)
        {
            originalPayment.Status = PaymentStatus.REFUNDED;
        }

        if (originalPayment.BookingId is Guid bookingId)
        {
            var booking = await db.Bookings.SingleOrDefaultAsync(b => b.Id == bookingId, ct);
            if (booking != null && booking.PaidAmount >= Math.Abs(refundAmount))
            {
                booking.PaidAmount -= Math.Abs(refundAmount);
            }
        }

        db.Payments.Add(refundPayment);

        db.AuditLogs.Add(new AuditLog
        {
            OrganizationId = orgId,
            UserId = userId,
            Action = "PAYMENT_REFUNDED",
            EntityType = "Payment",
            EntityId = refundPayment.Id,
            Before = null,
            After = JsonSerializer.Serialize(new { originalPaymentId = id, refundAmount, method = request.Method }, JsonOptions),
            Ip = HttpContext.Connection.RemoteIpAddress,
            CreatedAt = clock.UtcNow
        });

        await db.SaveChangesAsync(ct);

        return NoContent();
    }
}
