namespace Futsal.Domain.Identity;

public sealed class OtpCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Phone { get; set; }
    public required string CodeHash { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
