using System.Security.Claims;
using Futsal.Application.Abstractions;

namespace Futsal.Api.Identity;

public sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;
    public Guid? UserId => Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var value) ? value : null;
    public Guid? OrganizationId => Guid.TryParse(Principal?.FindFirstValue("org"), out var value) ? value : null;
    public string? Role => Principal?.FindFirstValue(ClaimTypes.Role);
    public bool IsSuperAdmin => Principal?.HasClaim("superadmin", "true") == true;
    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;
}
