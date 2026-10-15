namespace Futsal.Api.Contracts;

public sealed record StaffLoginRequest(string Identifier, string Password, Guid? OrganizationId = null,string? TwoFactorCode=null);
public sealed record OtpRequest(string Phone, Guid? VenueId = null, string? Language = null);
public sealed record OtpVerifyRequest(string Phone, string Code, string? FullName = null);
public sealed record RefreshRequest(string RefreshToken, Guid? OrganizationId = null);
public sealed record LogoutRequest(string RefreshToken);
public sealed record TokenResponse(
    Guid UserId,
    Guid? OrganizationId,
    string Role,
    string AccessToken,
    DateTimeOffset AccessExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshExpiresAt);
public sealed record CurrentUserResponse(Guid Id, string FullName, string? Email, string? Phone, Guid? OrganizationId, string Role);
