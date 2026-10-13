using Futsal.Application.Abstractions;

namespace Futsal.Api.Bookings;

public sealed class NoopBookingNotifier : IBookingNotifier
{
    public Task PublishAsync(BookingEvent message,CancellationToken cancellationToken)=>Task.CompletedTask;
}
