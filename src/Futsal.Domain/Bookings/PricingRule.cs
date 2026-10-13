using Futsal.Domain.Identity;

namespace Futsal.Domain.Bookings;

public sealed class PricingRule : IOrganizationScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid CourtId { get; set; }
    public string Name { get; set; } = "";
    public short[] Weekdays { get; set; } = [];
    public TimeOnly FromTime { get; set; }
    public TimeOnly ToTime { get; set; }
    public decimal PricePerSlot { get; set; }
    public int Priority { get; set; }
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
}
