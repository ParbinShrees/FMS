namespace Futsal.Domain.Identity;

public sealed class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public required string FullName { get; set; }
    public string? PasswordHash { get; set; }
    public bool IsPlatformAdmin { get; set; }
    public bool IsActive { get; set; } = true;
    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
