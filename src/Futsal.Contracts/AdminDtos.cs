namespace Futsal.Contracts;

public sealed record StaffLoginDto(string Identifier, string Password, Guid? OrganizationId = null);
public sealed record TokenDto(Guid UserId, Guid? OrganizationId, string Role, string AccessToken,
    DateTimeOffset AccessExpiresAt, string RefreshToken, DateTimeOffset RefreshExpiresAt);
public sealed record RefreshDto(string RefreshToken, Guid? OrganizationId = null);
public sealed record LogoutDto(string RefreshToken);
public sealed record VenueDto(Guid Id, string Name, string Slug, string Timezone);
public sealed record AvailabilitySlotDto(Guid CourtId, string CourtName, DateTimeOffset StartAt, DateTimeOffset EndAt,
    string Status, decimal Price);
public sealed record BookingQuoteDto(decimal TotalAmount, decimal DiscountAmount, decimal AdvanceRequired);
public sealed record StaffBookingDto(Guid CourtId, string Phone, string FullName, DateTimeOffset StartAt,
    DateTimeOffset EndAt, string? Notes = null, decimal? InitialPayment = null, string? InitialPaymentMethod = null);
public sealed record BookingDto(Guid Id, string BookingCode, string Status, DateTimeOffset StartAt, DateTimeOffset EndAt,
    DateTimeOffset? HoldExpiresAt, decimal TotalAmount, decimal DiscountAmount, decimal AdvanceRequired, decimal PaidAmount);
public sealed record AvailabilityEventDto(Guid VenueId, Guid CourtId, DateTimeOffset StartUtc, DateTimeOffset EndUtc,
    string Status, DateTimeOffset? HoldExpiresAtUtc);
