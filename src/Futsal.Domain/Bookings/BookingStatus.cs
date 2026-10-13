namespace Futsal.Domain.Bookings;

public enum BookingStatus { HELD, CONFIRMED, COMPLETED, CANCELLED, EXPIRED, NO_SHOW }
public enum BookingSource { ONLINE, STAFF, RECURRING }
public enum PaymentMethod { CASH, ESEWA, KHALTI, FONEPAY, BANK, OTHER }
public enum PaymentStatus { PENDING, COMPLETED, FAILED, NEEDS_REFUND, REFUNDED }

public sealed class BookingDomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
