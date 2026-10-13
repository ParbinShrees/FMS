using Futsal.Domain.Bookings;

namespace Futsal.Api.Contracts;

public sealed record CreateHoldRequest(Guid CourtId,DateTimeOffset StartAt,DateTimeOffset EndAt);
public sealed record StaffBookingRequest(Guid CourtId,string Phone,string FullName,DateTimeOffset StartAt,DateTimeOffset EndAt,string? Notes=null,decimal? InitialPayment=null,PaymentMethod? InitialPaymentMethod=null);
public sealed record RescheduleBookingRequest(DateTimeOffset StartAt,DateTimeOffset EndAt);
public sealed record CancelBookingRequest(string? Reason=null);
public sealed record RecordPaymentRequest(decimal Amount,PaymentMethod Method,string? Reference=null);
public sealed record BookingResponse(Guid Id,string BookingCode,BookingStatus Status,DateTimeOffset StartAt,DateTimeOffset EndAt,DateTimeOffset? HoldExpiresAt,decimal TotalAmount,decimal DiscountAmount,decimal AdvanceRequired,decimal PaidAmount);
public sealed record AvailabilitySlot(Guid CourtId,string CourtName,DateTimeOffset StartAt,DateTimeOffset EndAt,string Status,decimal Price);
