using System.Net;

namespace Futsal.Domain.Identity;

public sealed class AuditLog
{
    public long Id { get; set; }
    public Guid? OrganizationId { get; set; }
    public Guid? UserId { get; set; }
    public required string Action { get; set; }
    public required string EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public string? Before { get; set; }
    public string? After { get; set; }
    public IPAddress? Ip { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
