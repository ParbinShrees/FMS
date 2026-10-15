using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Futsal.Api.Contracts;
using Futsal.Api.Identity;
using Futsal.Application.Abstractions;
using Futsal.Domain.Bookings;
using Futsal.Domain.Identity;
using Futsal.Domain.Locations;
using Futsal.Infrastructure.Payments;
using Futsal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Futsal.Api.Payments;

public sealed class PaymentProcessingService(
    FutsalDbContext db,
    ICurrentUser current,
    IClock clock,
    IPaymentGatewayResolver gatewayResolver,
    IVenuePaymentProtector protector,
    IBookingNotifier notifier,
    ILogger<PaymentProcessingService> logger)
{
    private async Task RequireRealOrganization(Guid organizationId,CancellationToken ct){if(await DemoSafety.IsDemoAsync(db,organizationId,ct))throw new ApiException(409,"DEMO_GATEWAY_DISABLED","DEMO_GATEWAY_DISABLED");}
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<InitiatePaymentResponse> InitiateEsewaAsync(InitiatePaymentRequest request, IPAddress? ip, CancellationToken ct)
    {
        var userId = RequireCustomer();
        var booking = await db.Bookings.IgnoreQueryFilters()
            .SingleOrDefaultAsync(b => b.Id == request.BookingId, ct)
            ?? throw new ApiException(404, "NOT_FOUND", "Booking was not found.");

        await RequireRealOrganization(booking.OrganizationId,ct);
        var customer = await db.Customers.IgnoreQueryFilters()
            .SingleOrDefaultAsync(c => c.Id == booking.CustomerId && c.UserId == userId, ct)
            ?? throw new ApiException(403, "FORBIDDEN", "You do not own this booking.");

        if (booking.Status != BookingStatus.HELD || (booking.HoldExpiresAt != null && booking.HoldExpiresAt <= clock.UtcNow))
            throw new ApiException(409, "HOLD_EXPIRED", "Booking hold has expired.");

        var venue = await db.Venues.IgnoreQueryFilters()
            .SingleOrDefaultAsync(v => v.Id == booking.VenueId, ct)
            ?? throw new ApiException(404, "NOT_FOUND", "Venue was not found.");

        var settings = await db.VenuePaymentSettings.IgnoreQueryFilters()
            .SingleOrDefaultAsync(s => s.VenueId == venue.Id && s.Method == PaymentMethod.ESEWA, ct);

        if (settings == null || !settings.IsEnabled || string.IsNullOrWhiteSpace(settings.MerchantCode))
            throw new ApiException(400, "ONLINE_PAYMENT_DISABLED", "Online payment is disabled for this venue.");

        var secret = protector.Unprotect(settings.EncryptedSecret);

        // Server computes the advance amount from the booking! Client never supplies it!
        var advanceAmount = booking.AdvanceRequired;
        if (advanceAmount <= 0) advanceAmount = booking.TotalAmount;

        var txnUuid = $"{DateTimeOffset.UtcNow:yyMMddHHmmss}-{Guid.NewGuid():N}"[..30];

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            OrganizationId = booking.OrganizationId,
            BookingId = booking.Id,
            Method = PaymentMethod.ESEWA,
            Status = PaymentStatus.PENDING,
            Amount = advanceAmount,
            GatewayTxnUuid = txnUuid,
            CreatedAt = clock.UtcNow
        };

        db.Payments.Add(payment);
        AddAudit(booking.OrganizationId, userId, "PAYMENT_INITIATED", payment.Id, null, new { txnUuid, advanceAmount }, ip);
        await db.SaveChangesAsync(ct);

        var gateway = gatewayResolver.GetGateway(PaymentMethod.ESEWA);
        var paymentRequest = new PaymentRequest(
            BookingId: booking.Id,
            VenueId: venue.Id,
            Amount: advanceAmount,
            TransactionUuid: txnUuid,
            ProductCode: settings.MerchantCode,
            SecretKey: secret,
            SuccessUrl: "",
            FailureUrl: "",
            CustomerPhone: customer.Phone);

        var initiation = await gateway.InitiateAsync(paymentRequest, ct);

        return new InitiatePaymentResponse(
            GatewayUrl: initiation.GatewayUrl,
            Method: initiation.Method,
            FormFields: initiation.FormFields,
            TransactionUuid: initiation.TransactionUuid,
            Amount: initiation.Amount);
    }

    public async Task<VerifyPaymentResponse> VerifyEsewaAsync(VerifyPaymentRequest request, IPAddress? ip, CancellationToken ct)
    {
        var txnUuid = ExtractTransactionUuidFromBase64(request.Data);
        if (string.IsNullOrWhiteSpace(txnUuid))
            throw new ApiException(400, "VALIDATION_FAILED", "Could not extract transaction_uuid from payment data.");

        var payment = await db.Payments.IgnoreQueryFilters()
            .SingleOrDefaultAsync(p => p.GatewayTxnUuid == txnUuid, ct)
            ?? throw new ApiException(404, "NOT_FOUND", "Payment record not found.");

        var booking = await db.Bookings.IgnoreQueryFilters()
            .SingleOrDefaultAsync(b => b.Id == payment.BookingId, ct)
            ?? throw new ApiException(404, "NOT_FOUND", "Booking was not found.");

        await RequireRealOrganization(booking.OrganizationId,ct);
        // Idempotency: verifying the same transaction twice must never double-credit or double-confirm
        if (payment.Status == PaymentStatus.COMPLETED)
        {
            return new VerifyPaymentResponse(
                IsSuccess: true,
                BookingId: booking.Id,
                BookingCode: booking.BookingCode,
                NeedsRefund: false,
                Message: "Payment was already completed.");
        }

        if (payment.Status == PaymentStatus.NEEDS_REFUND)
        {
            return new VerifyPaymentResponse(
                IsSuccess: false,
                BookingId: booking.Id,
                BookingCode: booking.BookingCode,
                NeedsRefund: true,
                Message: "Slot was taken after hold expired. Payment is marked for refund.");
        }

        var venue = await db.Venues.IgnoreQueryFilters()
            .SingleAsync(v => v.Id == booking.VenueId, ct);

        var settings = await db.VenuePaymentSettings.IgnoreQueryFilters()
            .SingleOrDefaultAsync(s => s.VenueId == venue.Id && s.Method == PaymentMethod.ESEWA, ct);

        string productCode = settings?.MerchantCode ?? "EPAYTEST";
        string secretKey = settings != null && !string.IsNullOrWhiteSpace(settings.EncryptedSecret)
            ? protector.Unprotect(settings.EncryptedSecret)
            : "8gBm/:&EnhH.1/q";

        var gateway = gatewayResolver.GetGateway(PaymentMethod.ESEWA);
        var callback = new PaymentCallback(
            EncodedData: request.Data,
            ProductCode: productCode,
            SecretKey: secretKey,
            TransactionUuid: payment.GatewayTxnUuid,
            Amount: payment.Amount);

        var verification = await gateway.VerifyAsync(callback, ct);

        if (!verification.IsSuccess)
        {
            payment.Status = PaymentStatus.FAILED;
            payment.RawResponse = verification.RawResponse;
            AddAudit(booking.OrganizationId, current.UserId, "PAYMENT_FAILED", payment.Id, null, new { error = verification.ErrorMessage }, ip);
            await db.SaveChangesAsync(ct);
            throw new ApiException(400, "PAYMENT_VERIFICATION_FAILED", verification.ErrorMessage ?? "Payment verification failed.");
        }

        return await ProcessVerifiedPaymentAsync(payment, booking, venue, verification, ip, ct);
    }

    public async Task ProcessPendingReconciliationsAsync(CancellationToken ct)
    {
        var cutoff = clock.UtcNow.AddMinutes(-2);
        var pendingPayments = await db.Payments.IgnoreQueryFilters()
            .Where(p => p.Status == PaymentStatus.PENDING && p.CreatedAt <= cutoff && p.GatewayTxnUuid != null && p.BookingId != null)
            .OrderBy(p => p.CreatedAt)
            .Take(20)
            .ToListAsync(ct);

        foreach (var payment in pendingPayments)
        {
            try
            {
                var booking = await db.Bookings.IgnoreQueryFilters().SingleOrDefaultAsync(b => b.Id == payment.BookingId, ct);
                if (booking == null) continue;
                if(await DemoSafety.IsDemoAsync(db,booking.OrganizationId,ct))continue;

                var venue = await db.Venues.IgnoreQueryFilters().SingleOrDefaultAsync(v => v.Id == booking.VenueId, ct);
                if (venue == null) continue;

                var settings = await db.VenuePaymentSettings.IgnoreQueryFilters()
                    .SingleOrDefaultAsync(s => s.VenueId == venue.Id && s.Method == payment.Method, ct);

                var productCode = settings?.MerchantCode ?? "EPAYTEST";
                var gateway = gatewayResolver.GetGateway(payment.Method);

                PaymentVerification check;
                if (gateway is EsewaGateway esewa)
                {
                    check = await esewa.CheckStatusInternalAsync(payment.GatewayTxnUuid!, payment.Amount, productCode, ct);
                }
                else
                {
                    check = await gateway.CheckStatusAsync(payment.GatewayTxnUuid!, payment.Amount, ct);
                }

                if (check.IsSuccess && check.Status == PaymentStatus.COMPLETED)
                {
                    await ProcessVerifiedPaymentAsync(payment, booking, venue, check, null, ct);
                }
                else if (check.Status == PaymentStatus.FAILED)
                {
                    payment.Status = PaymentStatus.FAILED;
                    payment.RawResponse = check.RawResponse;
                    await db.SaveChangesAsync(ct);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Reconciliation failed for payment {PaymentId}", payment.Id);
            }
        }
    }

    public async Task<VerifyPaymentResponse> ProcessVerifiedPaymentAsync(
        Payment payment,
        Booking booking,
        Venue venue,
        PaymentVerification verification,
        IPAddress? ip,
        CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            if (db.Database.IsRelational())
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended('court:' || {booking.CourtId}, 0))", ct);
            }

            if (payment.Status == PaymentStatus.COMPLETED)
            {
                await tx.CommitAsync(ct);
                return new VerifyPaymentResponse(true, booking.Id, booking.BookingCode);
            }

            var isHoldActive = booking.Status == BookingStatus.HELD && (booking.HoldExpiresAt == null || booking.HoldExpiresAt > clock.UtcNow);

            if (isHoldActive)
            {
                booking.Confirm();
                booking.PaidAmount += payment.Amount;
                payment.Status = PaymentStatus.COMPLETED;
                payment.GatewayRef = verification.GatewayRef;
                payment.RawResponse = verification.RawResponse;
                payment.VerifiedAt = clock.UtcNow;

                AddAudit(booking.OrganizationId, current.UserId, "PAYMENT_VERIFIED", payment.Id, null, new { booking.Id, payment.Amount }, ip);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                await notifier.PublishAsync(new BookingEvent("SlotBooked", venue.Id, booking.CourtId, booking.StartAt, booking.EndAt, null, booking.Id), ct);

                return new VerifyPaymentResponse(true, booking.Id, booking.BookingCode);
            }
            else
            {
                // LATE PAYMENT RULE (AGENTS.md 7.6):
                var isSlotTaken = await db.Bookings.IgnoreQueryFilters()
                    .AnyAsync(b => b.CourtId == booking.CourtId
                                && b.Id != booking.Id
                                && b.Status != BookingStatus.CANCELLED
                                && b.Status != BookingStatus.EXPIRED
                                && b.StartAt < booking.EndAt
                                && b.EndAt > booking.StartAt, ct)
                    || await db.BlockedPeriods.IgnoreQueryFilters()
                    .AnyAsync(bp => bp.CourtId == booking.CourtId
                                 && bp.StartAt < booking.EndAt
                                 && bp.EndAt > booking.StartAt, ct);

                if (!isSlotTaken)
                {
                    booking.ConfirmLate();
                    booking.PaidAmount += payment.Amount;
                    payment.Status = PaymentStatus.COMPLETED;
                    payment.GatewayRef = verification.GatewayRef;
                    payment.RawResponse = verification.RawResponse;
                    payment.VerifiedAt = clock.UtcNow;

                    AddAudit(booking.OrganizationId, current.UserId, "PAYMENT_VERIFIED_LATE_CONFIRMED", payment.Id, null, new { booking.Id, payment.Amount }, ip);
                    await db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);

                    await notifier.PublishAsync(new BookingEvent("SlotBooked", venue.Id, booking.CourtId, booking.StartAt, booking.EndAt, null, booking.Id), ct);

                    return new VerifyPaymentResponse(true, booking.Id, booking.BookingCode);
                }
                else
                {
                    payment.Status = PaymentStatus.NEEDS_REFUND;
                    payment.GatewayRef = verification.GatewayRef;
                    payment.RawResponse = verification.RawResponse;
                    payment.VerifiedAt = clock.UtcNow;

                    AddAudit(booking.OrganizationId, current.UserId, "PAYMENT_NEEDS_REFUND", payment.Id, null, new
                    {
                        bookingId = booking.Id,
                        amount = payment.Amount,
                        reason = "Slot occupied after hold expired"
                    }, ip);

                    await db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);

                    return new VerifyPaymentResponse(false, booking.Id, booking.BookingCode, NeedsRefund: true, Message: "Slot was taken after hold expired. Payment marked for refund.");
                }
            }
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private static string? ExtractTransactionUuidFromBase64(string base64Data)
    {
        try
        {
            var clean = base64Data.Trim().Replace(" ", "+");
            var mod4 = clean.Length % 4;
            if (mod4 > 0) clean += new string('=', 4 - mod4);
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(clean));
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("transaction_uuid", out var tu) ? tu.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    private Guid RequireCustomer() => current.IsAuthenticated && current.Role == "Customer" && current.UserId is Guid id
        ? id
        : throw new ApiException(403, "FORBIDDEN", "Customer authentication is required.");

    private void AddAudit(Guid org, Guid? user, string action, Guid entityId, object? before, object? after, IPAddress? ip) =>
        db.AuditLogs.Add(new AuditLog
        {
            OrganizationId = org,
            UserId = user,
            Action = action,
            EntityType = "Payment",
            EntityId = entityId,
            Before = before is null ? null : JsonSerializer.Serialize(before, JsonOptions),
            After = after is null ? null : JsonSerializer.Serialize(after, JsonOptions),
            Ip = ip,
            CreatedAt = clock.UtcNow
        });
}
