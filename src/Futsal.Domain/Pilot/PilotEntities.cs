using Futsal.Domain.Identity;
namespace Futsal.Domain.Pilot;

public abstract class PilotRecord : IOrganizationScoped { public Guid Id { get; set; } = Guid.NewGuid(); public Guid OrganizationId { get; set; } }
public sealed class OnboardingState : PilotRecord { public int Step { get; set; } public string Draft { get; set; } = "{}"; public Guid? VenueId { get; set; } public DateTimeOffset UpdatedAt { get; set; } public DateTimeOffset? CompletedAt { get; set; } }
public sealed class StaffInvite : PilotRecord { public string Email { get; set; } = ""; public string Role { get; set; } = "Cashier"; public Guid VenueId { get; set; } public string TokenHash { get; set; } = ""; public DateTimeOffset ExpiresAt { get; set; } public DateTimeOffset? AcceptedAt { get; set; } }
public sealed class PilotFeedback : PilotRecord { public Guid SubmittedBy { get; set; } public string Category { get; set; } = "bug"; public string Text { get; set; } = ""; public string PageUrl { get; set; } = ""; public string Status { get; set; } = "NEW"; public byte[]? Screenshot { get; set; } public DateTimeOffset CreatedAt { get; set; } }
public sealed class PilotError : PilotRecord { public string CorrelationId { get; set; } = ""; public DateTimeOffset CreatedAt { get; set; } }
public sealed class BookingWatch : PilotRecord { public long OverlapCount { get; set; } public DateTimeOffset CheckedAt { get; set; } }
