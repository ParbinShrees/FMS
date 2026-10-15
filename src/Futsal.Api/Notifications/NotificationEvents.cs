using Futsal.Application.Abstractions;
using Futsal.Domain.Bookings;
using Futsal.Infrastructure.Notifications;
using Futsal.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
namespace Futsal.Api.Notifications;

public sealed class NotificationEvents(DbContextOptions<FutsalDbContext> options, IClock clock, IDataProtectionProvider protection, INotificationSender sender, ILogger<NotificationEvents> logger)
{
    public async Task PublishAsync(Guid bookingId, CancellationToken ct)
    {
        try
        {
            await using var db = new FutsalDbContext(options, new NotificationSystemUser(), clock);

            var b = await db.Bookings.SingleOrDefaultAsync(x => x.Id == bookingId, ct);
            if (b == null) return;
            var q = new NotificationQueue(db, clock, protection, sender);
            if (b.Status is BookingStatus.CONFIRMED or BookingStatus.CANCELLED) { var name = b.Status == BookingStatus.CONFIRMED ? "confirmation" : "cancellation"; await q.QueueBookingAsync(b, name, $"booking:{b.Id}:{name}", null, ct); }
            var payments = await db.Payments.Where(x => x.BookingId == b.Id && x.OrganizationId == b.OrganizationId && x.Status == PaymentStatus.COMPLETED && x.Amount > 0).ToListAsync(ct);
            foreach (var p in payments) await q.QueueBookingAsync(b, "payment", $"payment:{p.Id}", p.Amount, ct);

        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { logger.LogWarning("Notification event deferred to recovery job ({Type})", ex.GetType().Name); }
    }
    private sealed class NotificationSystemUser : ICurrentUser
    {
        public Guid? UserId => null; public Guid? OrganizationId => null; public string? Role => null; public bool IsSuperAdmin => true; public bool IsAuthenticated => false;
    }
}
