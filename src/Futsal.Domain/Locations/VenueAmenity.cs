namespace Futsal.Domain.Locations;

public sealed class VenueAmenity
{
    public Guid VenueId { get; set; }
    public Guid AmenityId { get; set; }
    public Venue Venue { get; set; } = null!;
    public Amenity Amenity { get; set; } = null!;
}
