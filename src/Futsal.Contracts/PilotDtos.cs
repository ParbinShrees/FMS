namespace Futsal.Contracts;

public sealed record ImportRequest(string Csv, bool Preview, Guid RunId);
public sealed record ImportRow(int Row, string Status, string Code, string[] Cells, Guid? BookingId = null);
public sealed record ImportReport(List<ImportRow> Rows) { public int Imported => Rows.Count(x => x.Status == "IMPORTED"); public int Skipped => Rows.Count(x => x.Status == "SKIPPED"); }
public sealed record FeedbackWrite(string Category, string Text, string PageUrl, string? ScreenshotBase64 = null);
public sealed record FeedbackView(Guid Id, Guid OrganizationId, string Organization, string Category, string Text, string PageUrl, string Status, bool HasScreenshot, DateTimeOffset CreatedAt);
public sealed record PilotCourt(string Name, decimal Price, int SlotMinutes = 60);
public sealed record PilotHours(short Weekday, TimeOnly Open, TimeOnly Close);
public sealed record PilotPrice(int CourtIndex, string Name, short[] Weekdays, TimeOnly From, TimeOnly To, decimal Price, int Priority);
public sealed record PilotInvite(string Email, string Role);
public sealed record OnboardingDraft(string Name, string Slug, Guid LocationId, string Address, List<PilotCourt> Courts, List<PilotHours> Hours, List<PilotPrice> Prices, int FreeCancelHours = 6, string AdvanceType = "PERCENT", decimal AdvanceValue = 30, List<PilotInvite>? Staff = null);
public sealed record OnboardingSave(int Step, OnboardingDraft Draft);
public sealed record OnboardingView(int Step, OnboardingDraft? Draft, Guid? VenueId, bool Complete);
public sealed record InviteLink(string Email, string Token);
public sealed record OnboardingResult(Guid VenueId, List<InviteLink> Invites);
public sealed record InviteAccept(string Token, string FullName, string Password);
public sealed record PilotDay(DateOnly Day, int Bookings, int Online, int Staff, int NoShows, int Cancellations);
public sealed record PilotSummary(Guid OrganizationId, string Name, Guid? VenueId, string? VenueName, bool Demo, List<PilotDay> Days, int FailedPayments, int SmsSent, int SmsFailed, DateTimeOffset? LastLogin, int Errors24Hours, long Overlaps, DateTimeOffset? WatchCheckedAt);

public sealed record DemoAccess(Guid Id, string Login, string Password, string VenueSlug);
