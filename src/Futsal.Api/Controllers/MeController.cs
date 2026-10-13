using System.Security.Claims;
using Futsal.Api.Contracts;
using Futsal.Application.Abstractions;
using Futsal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Futsal.Api.Controllers;

[ApiController]
[Route("api/v1/me")]
[Authorize]
public sealed class MeController(FutsalDbContext database, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CurrentUserResponse>> Get(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var user = await database.Users.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken)
            ?? throw new UnauthorizedAccessException();
        return Ok(new CurrentUserResponse(
            user.Id,
            user.FullName,
            user.Email,
            user.Phone,
            currentUser.OrganizationId,
            User.FindFirstValue(ClaimTypes.Role) ?? "Customer"));
    }
}
