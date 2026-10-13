namespace Futsal.Domain.Identity;

public interface IOrganizationScoped
{
    Guid OrganizationId { get; }
}
