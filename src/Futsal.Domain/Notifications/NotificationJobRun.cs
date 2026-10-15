namespace Futsal.Domain.Notifications;
/// <summary>Platform worker metadata; never exposed through tenant APIs.</summary>
public sealed class NotificationJobRun
{
    public string Name { get; set; } = "";
    public DateTimeOffset NextRun { get; set; }
}
