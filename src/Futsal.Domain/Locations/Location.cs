namespace Futsal.Domain.Locations;

public sealed class Location
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ParentId { get; set; }
    public LocationType Type { get; set; }
    public string NameEn { get; set; } = "";
    public string NameNe { get; set; } = "";
    public string Slug { get; set; } = "";
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public string Path { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public bool IsSeeded { get; set; }
    public int SortOrder { get; set; }
    public Location? Parent { get; set; }
    public ICollection<Location> Children { get; set; } = new List<Location>();
}
