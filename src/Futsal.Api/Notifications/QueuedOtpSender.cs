using Futsal.Application.Abstractions;
using Futsal.Infrastructure.Notifications;
namespace Futsal.Api.Notifications;

public sealed class QueuedOtpSender(NotificationQueue queue) : IOtpSender
{
    public Task SendForVenueAsync(string phone, string code, Guid? venueId, string? language, CancellationToken ct) => queue.QueueOtpAsync(phone, code, ct, venueId, language);
    public Task SendAsync(string phone, string code, CancellationToken ct) => queue.QueueOtpAsync(phone, code, ct);
}
