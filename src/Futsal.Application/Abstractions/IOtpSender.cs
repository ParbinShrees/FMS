namespace Futsal.Application.Abstractions;

public interface IOtpSender
{
    Task SendForVenueAsync(string phone, string code, Guid? venueId, string? language, CancellationToken ct) => SendAsync(phone, code, ct);
    Task SendAsync(string phone, string code, CancellationToken cancellationToken);
}
