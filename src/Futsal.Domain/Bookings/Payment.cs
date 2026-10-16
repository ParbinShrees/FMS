using Futsal.Domain.Identity;

namespace Futsal.Domain.Bookings;

public sealed class Payment : IOrganizationScoped
{
    public Guid? ReversalOf{get;set;}
    public Guid? PrepaymentBlockId{get;set;}
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid? BookingId { get; set; }
    public PaymentMethod Method { get; set; }
    public PaymentStatus Status { get; set; }
    public decimal Amount { get; set; }
    public string? GatewayTxnUuid { get; set; }
    public string? GatewayRef { get; set; }
    public string? RawResponse { get; set; }
    public Guid? ReceivedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
}
