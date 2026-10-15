namespace Futsal.Contracts;

public sealed record StaffLoginDto(string Identifier, string Password, Guid? OrganizationId = null);
public sealed record TokenDto(Guid UserId, Guid? OrganizationId, string Role, string AccessToken,
    DateTimeOffset AccessExpiresAt, string RefreshToken, DateTimeOffset RefreshExpiresAt);
public sealed record RefreshDto(string RefreshToken, Guid? OrganizationId = null);
public sealed record LogoutDto(string RefreshToken);
public sealed record VenueDto(Guid Id, string Name, string Slug, string Timezone,bool BsFirst=false,bool NepaliNumerals=false);
public sealed record AvailabilitySlotDto(Guid CourtId, string CourtName, DateTimeOffset StartAt, DateTimeOffset EndAt,
    string Status, decimal Price);
public sealed record BookingQuoteDto(decimal TotalAmount, decimal DiscountAmount, decimal AdvanceRequired);
public sealed record StaffBookingDto(Guid CourtId, string Phone, string FullName, DateTimeOffset StartAt,
    DateTimeOffset EndAt, string? Notes = null, decimal? InitialPayment = null, string? InitialPaymentMethod = null);
public sealed record BookingDto(Guid Id, string BookingCode, string Status, DateTimeOffset StartAt, DateTimeOffset EndAt,
    DateTimeOffset? HoldExpiresAt, decimal TotalAmount, decimal DiscountAmount, decimal AdvanceRequired, decimal PaidAmount);
public sealed record AvailabilityEventDto(Guid VenueId, Guid CourtId, DateTimeOffset StartUtc, DateTimeOffset EndUtc,
    string Status, DateTimeOffset? HoldExpiresAtUtc);

public sealed record PublicVenueDetailDto(
    Guid Id,
    string Name,
    string Slug,
    string? Address,
    decimal? Latitude,
    decimal? Longitude,
    string? PhotoPlaceholder,
    int HoldMinutes,
    int FreeCancelHours,
    string AdvanceType,
    decimal AdvanceValue,
    bool HasOnlinePayment,
    IReadOnlyList<PublicCourtDto> Courts,
    IReadOnlyList<string> Amenities,bool BsFirst=false,bool NepaliNumerals=false);

public sealed record PublicCourtDto(
    Guid Id,
    string Name,
    string? Surface,
    int SlotMinutes,
    decimal PricePerSlot);

public sealed record InitiatePaymentRequestDto(Guid BookingId);

public sealed record InitiatePaymentResponseDto(
    string GatewayUrl,
    string Method,
    IReadOnlyDictionary<string, string> FormFields,
    string TransactionUuid,
    decimal Amount);

public sealed record VerifyPaymentRequestDto(string Data);

public sealed record VerifyPaymentResponseDto(
    bool IsSuccess,
    Guid BookingId,
    string BookingCode,
    bool NeedsRefund = false,
    string? Message = null);

public sealed record VenuePaymentSettingsDto(
    bool IsEnabled,
    string MerchantCode,
    bool HasSecret,
    DateTimeOffset? UpdatedAt);

public sealed record UpdateVenuePaymentSettingsRequestDto(
    bool IsEnabled,
    string MerchantCode,
    string? SecretKey);

public sealed record NeedsRefundPaymentDto(
    Guid PaymentId,
    Guid OrganizationId,
    Guid? BookingId,
    string? BookingCode,
    string? CustomerName,
    string? CustomerPhone,
    decimal Amount,
    string Method,
    string? GatewayTxnUuid,
    string? GatewayRef,
    DateTimeOffset CreatedAt,
    DateTimeOffset? VerifiedAt);

