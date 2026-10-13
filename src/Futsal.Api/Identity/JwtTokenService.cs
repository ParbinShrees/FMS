using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Futsal.Api.Contracts;
using Futsal.Application.Abstractions;
using Futsal.Domain.Identity;
using Microsoft.IdentityModel.Tokens;

namespace Futsal.Api.Identity;

public sealed class JwtTokenService(IConfiguration configuration, IClock clock)
{
    private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(14);

    public string SigningKey => configuration["Jwt:SigningKey"]
        ?? throw new InvalidOperationException("JWT signing key is missing. Configure Jwt:SigningKey using user-secrets or an environment variable.");

    public (string AccessToken, DateTimeOffset AccessExpiresAt) CreateAccessToken(AppUser user, Guid? organizationId, string role)
    {
        var now = clock.UtcNow;
        var expiresAt = now.Add(AccessTokenLifetime);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Role, role),
            new("superadmin", user.IsPlatformAdmin ? "true" : "false")
        };
        if (user.Email is not null) claims.Add(new Claim(JwtRegisteredClaimNames.Email, user.Email));
        if (organizationId is not null) claims.Add(new Claim("org", organizationId.Value.ToString()));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));
        var token = new JwtSecurityToken(
            issuer: configuration["Jwt:Issuer"] ?? "FutsalPro",
            audience: configuration["Jwt:Audience"] ?? "FutsalProClients",
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    public (string RawToken, RefreshToken Entity) CreateRefreshToken(Guid userId, IPAddress? ipAddress)
    {
        var rawToken = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(64));
        var entity = new RefreshToken
        {
            UserId = userId,
            TokenHash = HashRefreshToken(rawToken),
            ExpiresAt = clock.UtcNow.Add(RefreshTokenLifetime),
            CreatedAt = clock.UtcNow,
            CreatedIp = ipAddress
        };
        return (rawToken, entity);
    }

    public static string HashRefreshToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
