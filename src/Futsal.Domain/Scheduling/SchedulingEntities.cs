using Futsal.Domain.Identity;
namespace Futsal.Domain.Scheduling;
public abstract class SchedulingRecord:IOrganizationScoped
{
 public Guid Id{get;set;}=Guid.NewGuid();public Guid OrganizationId{get;set;}public Guid VenueId{get;set;}
}
public sealed class RecurringBookingSeries:SchedulingRecord
{
 public Guid CourtId{get;set;}public Guid CustomerId{get;set;}public short Weekday{get;set;}public TimeOnly StartTimeLocal{get;set;}public int DurationSlots{get;set;}=1;public DateOnly StartDate{get;set;}public DateOnly? EndDate{get;set;}public string Status{get;private set;}="ACTIVE";public string PriceMode{get;set;}="CURRENT";public decimal? LockedPricePerOccurrence{get;set;}public bool ApplyRegularDiscount{get;set;}=true;public Guid CreatedBy{get;set;}public DateTimeOffset CreatedAt{get;set;}public string? Notes{get;set;}
 public void Pause(){if(Status!="ACTIVE")throw new ArgumentException("INVALID_SERIES_STATE");Status="PAUSED";}
 public void Resume(){if(Status!="PAUSED")throw new ArgumentException("INVALID_SERIES_STATE");Status="ACTIVE";}
 public void End(DateOnly before){if(Status=="ENDED")return;EndDate=before;Status="ENDED";}
}
public sealed class RecurringSeriesException:SchedulingRecord
{
 public Guid SeriesId{get;set;}public DateOnly Date{get;set;}public string Type{get;set;}="SKIPPED";public DateTimeOffset? MovedToStartUtc{get;set;}public string? Reason{get;set;}
}
public sealed class RecurringSeriesRevision:SchedulingRecord
{
 public Guid SeriesId{get;set;}public DateOnly FromDate{get;set;}public Guid CourtId{get;set;}public TimeOnly StartTimeLocal{get;set;}public int DurationSlots{get;set;}
}
public sealed class SeriesConflict:SchedulingRecord
{
 public Guid SeriesId{get;set;}public DateOnly Date{get;set;}public string Reason{get;set;}="BOOKED";public DateTimeOffset CreatedAt{get;set;}public DateTimeOffset? ResolvedAt{get;set;}
}
public sealed class SeriesPrepayment:SchedulingRecord
{
 public Guid SeriesId{get;set;}public int Weeks{get;set;}public decimal Amount{get;set;}public Guid CreatedBy{get;set;}public DateTimeOffset CreatedAt{get;set;}
}
public sealed class WaitlistEntry:SchedulingRecord
{
 public Guid? CourtId{get;set;}public Guid CustomerId{get;set;}public DateOnly Date{get;set;}public TimeOnly FromTimeLocal{get;set;}public TimeOnly ToTimeLocal{get;set;}public int SlotsNeeded{get;set;}=1;public string Status{get;private set;}="WAITING";public long Position{get;set;}public DateTimeOffset CreatedAt{get;set;}public Guid? OfferedBookingId{get;private set;}public DateTimeOffset? OfferExpiresAt{get;private set;}public bool RequiresApproval{get;set;}
 public void Offer(Guid bookingId,DateTimeOffset expires){if(Status!="WAITING")throw new ArgumentException("INVALID_WAITLIST_STATE");Status="OFFERED";OfferedBookingId=bookingId;OfferExpiresAt=expires;}
 public void Convert(){if(Status=="OFFERED")Status="CONVERTED";}
 public void Expire(){if(Status is "WAITING" or "OFFERED")Status="EXPIRED";}
 public void Cancel(){if(Status is not ("WAITING" or "OFFERED"))throw new ArgumentException("INVALID_WAITLIST_STATE");Status="CANCELLED";}
}
