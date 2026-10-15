using Futsal.Application.Abstractions;
using Futsal.Domain.Bookings;

namespace Futsal.Infrastructure.Payments;

public sealed class PaymentGatewayResolver(IEnumerable<IPaymentGateway> gateways) : IPaymentGatewayResolver
{
    public IPaymentGateway GetGateway(PaymentMethod method)
    {
        return gateways.FirstOrDefault(g => g.Method == method)
            ?? throw new NotSupportedException($"Payment method {method} is not supported.");
    }
}
