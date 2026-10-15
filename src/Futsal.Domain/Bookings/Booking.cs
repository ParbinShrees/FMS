using Futsal.Domain.Identity;

namespace Futsal.Domain.Bookings;

public sealed class Booking : IOrganizationScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid VenueId { get; set; }
    public Guid CourtId { get; set; }
    public Guid? CustomerId { get; set; }
    public string BookingCode { get; set; } = "";
    public DateTimeOffset StartAt { get; set; }
    public DateTimeOffset EndAt { get; set; }
    public BookingStatus Status { get; private set; }
    public BookingSource Source { get; set; }
    public DateTimeOffset? HoldExpiresAt { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal AdvanceRequired { get; set; }
    public decimal PaidAmount { get; set; }
    public string? Notes { get; set; }
    public string? CancelReason { get; set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static Booking NewHeld() => new() { Status = BookingStatus.HELD };
    public static Booking NewStaffConfirmed() => new() { Status = BookingStatus.CONFIRMED };

    public void Confirm() { Ensure(BookingStatus.HELD); Status = BookingStatus.CONFIRMED; HoldExpiresAt = null; }
    public void ConfirmLate()
    {
        if (Status is not (BookingStatus.HELD or BookingStatus.EXPIRED)) Invalid("Only a held or expired booking can be confirmed late.");
        Status = BookingStatus.CONFIRMED; HoldExpiresAt = null;
    }
    public void Expire() { Ensure(BookingStatus.HELD); Status = BookingStatus.EXPIRED; HoldExpiresAt = null; }
    public void Cancel(DateTimeOffset now, string? reason = null)
    {
        if (Status is not (BookingStatus.HELD or BookingStatus.CONFIRMED)) Invalid("Only a held or confirmed booking can be cancelled.");
        Status = BookingStatus.CANCELLED; CancelledAt = now; CancelReason = reason; HoldExpiresAt = null; UpdatedAt = now;
    }
    public void Complete() { Ensure(BookingStatus.CONFIRMED); Status = BookingStatus.COMPLETED; }
    public void MarkNoShow() { Ensure(BookingStatus.CONFIRMED); Status = BookingStatus.NO_SHOW; }
    public void Reschedule(DateTimeOffset start, DateTimeOffset end, DateTimeOffset now)
    {
        Ensure(BookingStatus.CONFIRMED);
        if (end <= start) Invalid("Booking end must be after start.");
        StartAt = start; EndAt = end; UpdatedAt = now;
    }
    private void Ensure(BookingStatus expected) { if (Status != expected) Invalid($"Cannot transition {Status} to this state."); }
    private static void Invalid(string message) => throw new BookingDomainException("INVALID_BOOKING_TRANSITION", message);
}
