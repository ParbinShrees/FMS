namespace Futsal.Application.Abstractions;

public sealed record BookingEvent(string Name,Guid VenueId,Guid CourtId,DateTimeOffset StartAt,DateTimeOffset EndAt,DateTimeOffset? HoldExpiresAt=null,Guid? BookingId=null);
public interface IBookingNotifier { Task PublishAsync(BookingEvent message,CancellationToken cancellationToken); }
