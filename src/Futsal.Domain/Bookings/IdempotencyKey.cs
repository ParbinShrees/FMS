namespace Futsal.Domain.Bookings;

public sealed class IdempotencyKey
{
    public string Key { get; set; } = "";
    public Guid UserId { get; set; }
    public string Endpoint { get; set; } = "";
    public int? ResponseStatus { get; set; }
    public string? ResponseBody { get; set; }
    public string RequestHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
