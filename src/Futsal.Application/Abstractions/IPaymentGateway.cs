using Futsal.Domain.Bookings;

namespace Futsal.Application.Abstractions;

public sealed record PaymentRequest(
    Guid BookingId,
    Guid VenueId,
    decimal Amount,
    string TransactionUuid,
    string ProductCode,
    string SecretKey,
    string SuccessUrl,
    string FailureUrl,
    string? CustomerPhone = null);

public sealed record PaymentInitiation(
    string GatewayUrl,
    string Method,
    IReadOnlyDictionary<string, string> FormFields,
    string TransactionUuid,
    decimal Amount);

public sealed record PaymentCallback(
    string EncodedData,
    string? ProductCode = null,
    string? SecretKey = null,
    string? TransactionUuid = null,
    decimal? Amount = null);

public sealed record PaymentVerification(
    PaymentStatus Status,
    string TransactionUuid,
    string? GatewayRef,
    decimal Amount,
    string? RawResponse,
    bool IsSuccess,
    string? ErrorMessage = null);

public interface IPaymentGateway
{
    PaymentMethod Method { get; }
    Task<PaymentInitiation> InitiateAsync(PaymentRequest request, CancellationToken ct);
    Task<PaymentVerification> VerifyAsync(PaymentCallback callback, CancellationToken ct);
    Task<PaymentVerification> CheckStatusAsync(string transactionUuid, decimal amount, CancellationToken ct);
}

public interface IPaymentGatewayResolver
{
    IPaymentGateway GetGateway(PaymentMethod method);
}

public interface IVenuePaymentProtector
{
    string Protect(string secret);
    string Unprotect(string encryptedSecret);
}
