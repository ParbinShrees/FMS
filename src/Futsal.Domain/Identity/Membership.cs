namespace Futsal.Domain.Identity;

public sealed class Membership : IOrganizationScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }
    public OrganizationRole Role { get; set; }
    public Guid? VenueId { get; set; }
}
