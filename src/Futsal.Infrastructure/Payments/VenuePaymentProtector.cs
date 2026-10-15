using Futsal.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;

namespace Futsal.Infrastructure.Payments;

public sealed class VenuePaymentProtector(IDataProtectionProvider provider) : IVenuePaymentProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("FutsalPro.VenuePaymentSettings.v1");

    public string Protect(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        return _protector.Protect(secret);
    }

    public string Unprotect(string encryptedSecret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(encryptedSecret);
        return _protector.Unprotect(encryptedSecret);
    }
}
