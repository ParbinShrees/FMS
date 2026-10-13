using System.Data;
using System.Net;
using System.Security.Cryptography;
using Futsal.Api.Contracts;
using Futsal.Application.Abstractions;
using Futsal.Domain.Identity;
using Futsal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Futsal.Api.Identity;

public sealed class AuthService(
    FutsalDbContext database,
    PasswordHasher<AppUser> passwordHasher,
    PasswordHasher<OtpCode> otpHasher,
    JwtTokenService tokenService,
    IOtpSender otpSender,
    IClock clock,
    ILogger<AuthService> logger)
{
    private static readonly TimeSpan StaffLockDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan OtpLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan OtpSendWindow = TimeSpan.FromHours(1);
    private const int MaxFailedStaffLogins = 5;
    private const int MaxOtpSendsPerHour = 3;
    private const int MaxOtpAttempts = 5;

    public async Task<TokenResponse> StaffLoginAsync(StaffLoginRequest request, IPAddress? ip, CancellationToken cancellationToken)
    {
        var identifier = request.Identifier.Trim();
        var normalizedIdentifier = identifier.ToLowerInvariant();
        var user = await database.Users.FirstOrDefaultAsync(
            x => x.Email == normalizedIdentifier || x.Phone == identifier,
            cancellationToken);
        var now = clock.UtcNow;
        if (user is null || !user.IsActive || user.IsPlatformAdmin && user.PasswordHash is null)
        {
            await WriteAuditAsync(request.OrganizationId, user?.Id, "LOGIN_FAILED", "User", user?.Id, ip, cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            throw new ApiException(StatusCodes.Status401Unauthorized, "INVALID_CREDENTIALS", "Email/phone or password is incorrect.");
        }

        if (user.LockedUntil > now)
        {
            await WriteAuditAsync(request.OrganizationId, user.Id, "LOGIN_FAILED", "User", user.Id, ip, cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            throw new ApiException(StatusCodes.Status423Locked, "ACCOUNT_LOCKED", "Account is temporarily locked. Try again later.");
        }

        if (user.LockedUntil is not null)
        {
            user.LockedUntil = null;
            user.FailedLoginCount = 0;
        }

        var verified = user.PasswordHash is not null && passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) != PasswordVerificationResult.Failed;
        if (!verified)
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedStaffLogins) user.LockedUntil = now.Add(StaffLockDuration);
            await WriteAuditAsync(request.OrganizationId, user.Id, "LOGIN_FAILED", "User", user.Id, ip, cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            if (user.LockedUntil is not null)
                throw new ApiException(StatusCodes.Status423Locked, "ACCOUNT_LOCKED", "Account is temporarily locked for 15 minutes.");
            throw new ApiException(StatusCodes.Status401Unauthorized, "INVALID_CREDENTIALS", "Email/phone or password is incorrect.");
        }

        var membership = user.IsPlatformAdmin
            ? null
            : await ResolveMembershipAsync(user.Id, request.OrganizationId, cancellationToken);
        if (!user.IsPlatformAdmin && membership is null)
        {
            await WriteAuditAsync(request.OrganizationId, user.Id, "LOGIN_FAILED", "User", user.Id, ip, cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            throw new ApiException(StatusCodes.Status401Unauthorized, "INVALID_CREDENTIALS", "Email/phone or password is incorrect.");
        }

        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        var session = await IssueSessionAsync(user, membership?.OrganizationId, user.IsPlatformAdmin ? "SuperAdmin" : RoleName(membership!.Role), ip, cancellationToken);
        await WriteAuditAsync(membership?.OrganizationId, user.Id, "LOGIN_SUCCEEDED", "User", user.Id, ip, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Staff login succeeded for user {UserId}", user.Id);
        return session;
    }

    public async Task<TokenResponse> RefreshAsync(RefreshRequest request, IPAddress? ip, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            throw new ApiException(StatusCodes.Status401Unauthorized, "INVALID_REFRESH_TOKEN", "Refresh token is invalid.");

        await using var transaction = database.Database.IsRelational()
            ? await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        var tokenHash = JwtTokenService.HashRefreshToken(request.RefreshToken);
        var oldToken = await database.RefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);
        if (oldToken is null)
            throw new ApiException(StatusCodes.Status401Unauthorized, "INVALID_REFRESH_TOKEN", "Refresh token is invalid.");

        if (oldToken.RevokedAt is not null)
        {
            var now = clock.UtcNow;
            var activeTokens = await database.RefreshTokens.Where(x => x.UserId == oldToken.UserId && x.RevokedAt == null).ToListAsync(cancellationToken);
            foreach (var active in activeTokens)
            {
                active.RevokedAt = now;
                await WriteAuditAsync(null, oldToken.UserId, "TOKEN_REVOKED", "RefreshToken", active.Id, ip, cancellationToken);
            }
            await WriteAuditAsync(null, oldToken.UserId, "REFRESH_TOKEN_REUSE", "RefreshToken", oldToken.Id, ip, cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            throw new ApiException(StatusCodes.Status401Unauthorized, "REFRESH_TOKEN_REUSED", "Refresh token reuse was detected. Sign in again.");
        }

        var user = await database.Users.SingleOrDefaultAsync(x => x.Id == oldToken.UserId && x.IsActive, cancellationToken)
            ?? throw new ApiException(StatusCodes.Status401Unauthorized, "INVALID_REFRESH_TOKEN", "Refresh token is invalid.");
        if (oldToken.ExpiresAt <= clock.UtcNow)
            throw new ApiException(StatusCodes.Status401Unauthorized, "INVALID_REFRESH_TOKEN", "Refresh token has expired.");

        Membership? membership = null;
        if (!user.IsPlatformAdmin && request.OrganizationId is not null)
            membership = await ResolveMembershipAsync(user.Id, request.OrganizationId, cancellationToken);
        else if (!user.IsPlatformAdmin && !string.IsNullOrEmpty(user.PasswordHash))
            membership = await ResolveMembershipAsync(user.Id, request.OrganizationId, cancellationToken);

        if (!user.IsPlatformAdmin && membership is null)
            throw new ApiException(StatusCodes.Status401Unauthorized, "INVALID_REFRESH_TOKEN", "Refresh token is invalid.");

        var role = user.IsPlatformAdmin ? "SuperAdmin" : membership is null ? "Customer" : RoleName(membership.Role);
        var session = await IssueSessionAsync(user, membership?.OrganizationId, role, ip, cancellationToken);
        oldToken.RevokedAt = clock.UtcNow;
        oldToken.ReplacedBy = database.RefreshTokens.Local.Single(x => x.TokenHash == JwtTokenService.HashRefreshToken(session.RefreshToken)).Id;
        await WriteAuditAsync(membership?.OrganizationId, user.Id, "TOKEN_REVOKED", "RefreshToken", oldToken.Id, ip, cancellationToken);
        await WriteAuditAsync(membership?.OrganizationId, user.Id, "TOKEN_ROTATED", "RefreshToken", oldToken.Id, ip, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return session;
    }

    public async Task LogoutAsync(LogoutRequest request, IPAddress? ip, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken)) return;
        var hash = JwtTokenService.HashRefreshToken(request.RefreshToken);
        var token = await database.RefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);
        if (token is null || token.RevokedAt is not null) return;
        token.RevokedAt = clock.UtcNow;
        await WriteAuditAsync(null, token.UserId, "TOKEN_REVOKED", "RefreshToken", token.Id, ip, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task RequestOtpAsync(OtpRequest request, CancellationToken cancellationToken)
    {
        var phone = NormalizePhone(request.Phone);
        var now = clock.UtcNow;
        var sendCount = await database.OtpCodes.CountAsync(x => x.Phone == phone && x.CreatedAt > now.Subtract(OtpSendWindow), cancellationToken);
        if (sendCount >= MaxOtpSendsPerHour)
            throw new ApiException(StatusCodes.Status429TooManyRequests, "RATE_LIMITED", "Too many OTP requests. Try again later.");

        var outstandingCodes = await database.OtpCodes.Where(x => x.Phone == phone && x.ConsumedAt == null).ToListAsync(cancellationToken);
        foreach (var outstanding in outstandingCodes) outstanding.ConsumedAt = now;

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        var otp = new OtpCode
        {
            Phone = phone,
            CodeHash = string.Empty,
            ExpiresAt = now.Add(OtpLifetime),
            CreatedAt = now
        };
        otp.CodeHash = otpHasher.HashPassword(otp, code);
        database.OtpCodes.Add(otp);
        await database.SaveChangesAsync(cancellationToken);
        await otpSender.SendAsync(phone, code, cancellationToken);
    }

    public async Task<TokenResponse> VerifyOtpAsync(OtpVerifyRequest request, IPAddress? ip, CancellationToken cancellationToken)
    {
        var phone = NormalizePhone(request.Phone);
        var now = clock.UtcNow;
        var otp = await database.OtpCodes.Where(x => x.Phone == phone && x.ConsumedAt == null)
            .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (otp is null || otp.ExpiresAt <= now || otp.Attempts >= MaxOtpAttempts)
        {
            await WriteAuditAsync(null, null, "LOGIN_FAILED", "OtpCode", null, ip, cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            throw new ApiException(StatusCodes.Status401Unauthorized, "OTP_INVALID", "The OTP is invalid or expired.");
        }

        otp.Attempts++;
        if (otpHasher.VerifyHashedPassword(otp, otp.CodeHash, request.Code) == PasswordVerificationResult.Failed)
        {
            var userId = await database.Users.Where(x => x.Phone == phone).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(cancellationToken);
            await WriteAuditAsync(null, userId, "LOGIN_FAILED", "OtpCode", otp.Id, ip, cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            throw new ApiException(StatusCodes.Status401Unauthorized, "OTP_INVALID", "The OTP is invalid or expired.");
        }

        otp.ConsumedAt = now;
        var user = await database.Users.SingleOrDefaultAsync(x => x.Phone == phone, cancellationToken);
        if (user is not null)
        {
            var hasStaffMembership = await database.Memberships.IgnoreQueryFilters().AnyAsync(x => x.UserId == user.Id, cancellationToken);
            if (hasStaffMembership || user.IsPlatformAdmin)
                throw new ApiException(StatusCodes.Status403Forbidden, "CUSTOMER_LOGIN_NOT_ALLOWED", "Use staff login for this account.");
        }
        else
        {
            user = new AppUser
            {
                Phone = phone,
                FullName = string.IsNullOrWhiteSpace(request.FullName) ? "Customer" : request.FullName.Trim(),
                CreatedAt = now
            };
            database.Users.Add(user);
        }

        var session = await IssueSessionAsync(user, null, "Customer", ip, cancellationToken);
        await WriteAuditAsync(null, user.Id, "CUSTOMER_LOGIN_SUCCEEDED", "User", user.Id, ip, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        return session;
    }

    private async Task<TokenResponse> IssueSessionAsync(AppUser user, Guid? organizationId, string role, IPAddress? ip, CancellationToken cancellationToken)
    {
        var (accessToken, accessExpiresAt) = tokenService.CreateAccessToken(user, organizationId, role);
        var (rawRefreshToken, refreshEntity) = tokenService.CreateRefreshToken(user.Id, ip);
        database.RefreshTokens.Add(refreshEntity);
        return new TokenResponse(user.Id, organizationId, role, accessToken, accessExpiresAt, rawRefreshToken, refreshEntity.ExpiresAt);
    }

    private Task<Membership?> FindMembershipAsync(Guid userId, Guid? organizationId, CancellationToken cancellationToken)
    {
        var query = database.Memberships.IgnoreQueryFilters().Where(x => x.UserId == userId);
        if (organizationId is not null) query = query.Where(x => x.OrganizationId == organizationId);
        return query.OrderBy(x => x.OrganizationId).FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<Membership?> ResolveMembershipAsync(Guid userId, Guid? organizationId, CancellationToken cancellationToken)
    {
        if (organizationId is not null) return await FindMembershipAsync(userId, organizationId, cancellationToken);
        var memberships = await database.Memberships.IgnoreQueryFilters().Where(x => x.UserId == userId)
            .OrderBy(x => x.OrganizationId).Take(2).ToListAsync(cancellationToken);
        if (memberships.Count > 1)
            throw new ApiException(StatusCodes.Status400BadRequest, "ORGANIZATION_REQUIRED", "Specify an organization because this user belongs to more than one organization.");
        return memberships.SingleOrDefault();
    }

    private Task WriteAuditAsync(Guid? organizationId, Guid? userId, string action, string entityType, Guid? entityId, IPAddress? ip, CancellationToken cancellationToken)
    {
        database.AuditLogs.Add(new AuditLog
        {
            OrganizationId = organizationId,
            UserId = userId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Ip = ip,
            CreatedAt = clock.UtcNow
        });
        return Task.CompletedTask;
    }

    private static string RoleName(OrganizationRole role) => role switch
    {
        OrganizationRole.OWNER => "Owner",
        OrganizationRole.MANAGER => "Manager",
        OrganizationRole.CASHIER => "Cashier",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown organization role.")
    };

    private static string NormalizePhone(string phone)
    {
        var normalized = phone.Trim();
        if (normalized.Length is < 9 or > 16 || normalized[0] != '+' || normalized.Skip(1).Any(x => !char.IsAsciiDigit(x)))
            throw new ApiException(StatusCodes.Status400BadRequest, "VALIDATION_FAILED", "Phone number must be in E.164 format.");
        return normalized;
    }
}
