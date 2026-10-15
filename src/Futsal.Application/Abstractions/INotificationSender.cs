namespace Futsal.Application.Abstractions;

public sealed record NotificationMessage(string Phone, string Text, string Reference, bool IsOtp = false);
public interface INotificationSender { Task SendAsync(NotificationMessage message, CancellationToken ct); }
