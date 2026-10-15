using Futsal.Domain.Bookings;
using Futsal.Domain.Identity;

namespace Futsal.Domain.Locations;

public sealed class VenuePaymentSettings : IOrganizationScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid VenueId { get; set; }
    public PaymentMethod Method { get; set; } = PaymentMethod.ESEWA;
    public bool IsEnabled { get; set; }
    public string MerchantCode { get; set; } = "";
    public string EncryptedSecret { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Venue Venue { get; set; } = null!;
}
