namespace Futsal.Domain.Locations;

public sealed class Amenity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string NameEn { get; set; } = "";
    public bool IsActive { get; set; } = true;
}
