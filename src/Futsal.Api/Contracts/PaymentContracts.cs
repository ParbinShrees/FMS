namespace Futsal.Api.Contracts;

public sealed record InitiatePaymentRequest(Guid BookingId);

public sealed record InitiatePaymentResponse(
    string GatewayUrl,
    string Method,
    IReadOnlyDictionary<string, string> FormFields,
    string TransactionUuid,
    decimal Amount);

public sealed record VerifyPaymentRequest(string Data);

public sealed record VerifyPaymentResponse(
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

public sealed record UpdateVenuePaymentSettingsRequest(
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
