using Futsal.Domain.Identity;

namespace Futsal.Domain.Bookings;

public sealed class BlockedPeriod : IOrganizationScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid CourtId { get; set; }
    public DateTimeOffset StartAt { get; set; }
    public DateTimeOffset EndAt { get; set; }
    public string? Reason { get; set; }
}
