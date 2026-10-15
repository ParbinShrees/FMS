using Futsal.Domain.Identity;
namespace Futsal.Domain.Notifications;

public sealed class Notification : IOrganizationScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid? VenueId { get; set; }
    public Guid? BookingId { get; set; }
    public Guid? CustomerId { get; set; }
    public string Channel { get; set; } = "SMS";
    public string Template { get; set; } = "";
    public string UniqueKey { get; set; } = "";
    public string Recipient { get; set; } = "";
    public string Payload { get; set; } = "";
    public string Status { get; set; } = "QUEUED";
    public int Attempts { get; set; }
    public int Segments { get; set; }
    public DateTimeOffset ScheduledFor { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? Error { get; set; }
    public void Fail(DateTimeOffset now) { Attempts++; Status = Attempts >= 5 ? "FAILED" : "QUEUED"; ScheduledFor = now.AddMinutes(Math.Pow(2, Attempts)); Error = "SMS_SEND_FAILED"; }
}
public sealed class NotificationSettings : IOrganizationScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid VenueId { get; set; }
    public DateTimeOffset EnabledAt { get; set; }
    public bool ConfirmationEnabled { get; set; } = true;
    public bool PaymentEnabled { get; set; } = true;
    public bool CancellationEnabled { get; set; } = true;
    public bool ReminderEnabled { get; set; } = true;
    public bool DueEnabled { get; set; }
    public bool IncludeBsDate { get; set; }
    public int ReminderHours { get; set; } = 2;
    public int DailyLimit { get; set; } = 100;
    public int MonthlyLimit { get; set; } = 2000;
    public string Language { get; set; } = "en";
    public string? Alert { get; set; }
}
public sealed class NotificationTemplate : IOrganizationScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid VenueId { get; set; }
    public string Name { get; set; } = "";
    public string Language { get; set; } = "en";
    public string Text { get; set; } = "";
}
public sealed class SmsUsage : IOrganizationScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid VenueId { get; set; }
    public DateOnly Day { get; set; }
    public int Segments { get; set; }
    public int DeliveredSegments { get; set; }
}
