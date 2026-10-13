using Futsal.Domain.Identity;

namespace Futsal.Domain.Bookings;

public sealed class Customer : IOrganizationScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid? UserId { get; set; }
    public string FullName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string? Notes { get; set; }
    public bool IsRegular { get; set; }
    public decimal DiscountPercent { get; set; }
    public int NoShowCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
