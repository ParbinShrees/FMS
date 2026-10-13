namespace Futsal.Domain.Locations;

public sealed class Court
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid VenueId { get; set; }
    public string Name { get; set; } = "";
    public string? Surface { get; set; }
    public decimal PricePerSlot { get; set; }
    public bool IsActive { get; set; } = true;
    public Venue Venue { get; set; } = null!;
}
