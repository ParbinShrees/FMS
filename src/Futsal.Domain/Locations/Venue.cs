using Futsal.Domain.Identity;

namespace Futsal.Domain.Locations;

public sealed class Venue : IOrganizationScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid LocationId { get; set; }
    public Guid CityId { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? Address { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public string? PhotoPlaceholder { get; set; }
    public bool IsPublic { get; set; } = true;
    public Location Location { get; set; } = null!;
    public ICollection<Court> Courts { get; set; } = new List<Court>();
    public ICollection<VenueAmenity> VenueAmenities { get; set; } = new List<VenueAmenity>();
}
