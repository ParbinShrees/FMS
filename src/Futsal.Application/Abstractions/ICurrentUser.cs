namespace Futsal.Application.Abstractions;

public interface ICurrentUser
{
    Guid? UserId { get; }
    Guid? OrganizationId { get; }
    string? Role { get; }
    bool IsSuperAdmin { get; }
    bool IsAuthenticated { get; }
}
