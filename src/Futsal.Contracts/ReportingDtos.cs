namespace Futsal.Contracts;

public sealed record ReportRow(string Category, string Label, decimal Amount, int Count, decimal Value, string? Detail = null);
public sealed record ReportResult(string Kind, DateOnly From, DateOnly To, IReadOnlyList<ReportRow> Rows);
public sealed record NotificationSettingsDto(bool ConfirmationEnabled, bool PaymentEnabled, bool CancellationEnabled, bool ReminderEnabled, bool DueEnabled, bool IncludeBsDate, int ReminderHours, int DailyLimit, int MonthlyLimit, string Language, string? Alert);
public sealed record TemplateDto(string Name, string Language, string Text);
public sealed record NotificationLogDto(Guid Id, string Status, string Recipient, string Template, int Attempts, string? Error, DateTimeOffset ScheduledFor);
public sealed record SmsPreviewDto(string Text, int Characters, int Units, int Segments, bool Unicode);

public sealed record SmsUsageDto(int Year, int Month, int Segments, int DeliveredSegments);
