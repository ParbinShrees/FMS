using Futsal.Application.Abstractions;
using Futsal.Domain.Identity;
using Futsal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Futsal.Api.Authorization;

public sealed class OrganizationRoleRequirement(params OrganizationRole[] allowedRoles) : IAuthorizationRequirement
{
    public IReadOnlyCollection<OrganizationRole> AllowedRoles { get; } = allowedRoles;
}

public sealed class OrganizationRoleHandler(FutsalDbContext database, ICurrentUser currentUser)
    : AuthorizationHandler<OrganizationRoleRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, OrganizationRoleRequirement requirement)
    {
        if (currentUser.IsSuperAdmin)
        {
            context.Succeed(requirement);
            return;
        }

        if (currentUser.UserId is not Guid userId || currentUser.OrganizationId is not Guid organizationId) return;

        if (!Enum.TryParse<OrganizationRole>(currentUser.Role, ignoreCase: true, out var claimedRole)
            || !requirement.AllowedRoles.Contains(claimedRole)) return;

        var hasRequiredMembership = await database.Memberships.AnyAsync(
            membership => membership.UserId == userId
                && membership.OrganizationId == organizationId
                && membership.Role == claimedRole);
        if (hasRequiredMembership) context.Succeed(requirement);
    }
}
