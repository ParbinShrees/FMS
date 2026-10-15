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
    public bool IsActive { get; set; } = true;
    public string Timezone { get; set; } = "Asia/Kathmandu";
    public int HoldMinutes { get; set; } = 10;
    public int FreeCancelHours { get; set; } = 6;
    public string AdvanceType { get; set; } = "PERCENT";
    public decimal AdvanceValue { get; set; } = 30;
    public Location Location { get; set; } = null!;
    public ICollection<Court> Courts { get; set; } = new List<Court>();
    public ICollection<VenueAmenity> VenueAmenities { get; set; } = new List<VenueAmenity>();
    public ICollection<VenuePaymentSettings> PaymentSettings { get; set; } = new List<VenuePaymentSettings>();
}
