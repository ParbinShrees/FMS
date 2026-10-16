using Futsal.Application.Abstractions;
using Futsal.Application.Scheduling;
using Futsal.Domain.Bookings;
using Futsal.Domain.Locations;
using Microsoft.EntityFrameworkCore;
namespace Futsal.Api.Scheduling;
public sealed partial class WaitlistService
{
 // Internal worker entry point. HTTP callers never choose another tenant's dispatch context.
 public async Task DispatchAsync(Guid venueId,DateOnly date,CancellationToken ct)
 {
  var events=new List<BookingEvent>();await using var tx=await db.Database.BeginTransactionAsync(ct);await writer.LockAsync("waitlist-venue:"+venueId,ct);var venue=await db.Venues.SingleOrDefaultAsync(x=>x.Id==venueId,ct);if(venue==null||!venue.IsActive){await tx.CommitAsync(ct);return;}await Security.SubscriptionGuard.RequireWritableAsync(db,clock,venue.OrganizationId,ct);
  var offered=await db.WaitlistEntries.Where(x=>x.VenueId==venueId&&x.Date==date&&x.Status=="OFFERED").ToListAsync(ct);
  foreach(var entry in offered)
  {
   var booking=await db.Bookings.SingleOrDefaultAsync(x=>x.Id==entry.OfferedBookingId,ct);if(booking==null){entry.Expire();continue;}
   if(booking.Status==BookingStatus.HELD&&booking.HoldExpiresAt<=clock.UtcNow)
   {
    await writer.LockAsync("court:"+booking.CourtId,ct);await db.Entry(booking).ReloadAsync(ct);if(booking.Status==BookingStatus.HELD&&booking.HoldExpiresAt<=clock.UtcNow){booking.Expire();booking.UpdatedAt=clock.UtcNow;writer.Audit(venue.OrganizationId,null,"WAITLIST_HOLD_EXPIRED","Booking",booking.Id);events.Add(SchedulingWriter.Event("SlotReleased",booking));}
   }
   if(booking.Status is BookingStatus.CONFIRMED or BookingStatus.COMPLETED or BookingStatus.NO_SHOW){entry.Convert();writer.Audit(venue.OrganizationId,null,"WAITLIST_CONVERTED","WaitlistEntry",entry.Id);}
   else if(booking.Status is BookingStatus.EXPIRED or BookingStatus.CANCELLED){entry.Expire();writer.Audit(venue.OrganizationId,null,"WAITLIST_OFFER_EXPIRED","WaitlistEntry",entry.Id);}
  }
  await db.SaveChangesAsync(ct);
  var waiting=await db.WaitlistEntries.Where(x=>x.VenueId==venueId&&x.Date==date&&x.Status=="WAITING").OrderBy(x=>x.Position).ToListAsync(ct);
  foreach(var entry in waiting)
  {
   var courts=await db.Courts.Where(x=>x.VenueId==venueId&&x.IsActive&&(entry.CourtId==null||x.Id==entry.CourtId)).OrderBy(x=>x.Id).ToListAsync(ct);(Court Court,DateTimeOffset Start,DateTimeOffset End)? match=null;
   foreach(var court in courts)
   {
    var hours=await db.CourtHours.SingleOrDefaultAsync(x=>x.CourtId==court.Id&&x.Weekday==(short)date.DayOfWeek,ct);if(hours==null)continue;
    foreach(var start in Candidates(date,entry.FromTimeLocal,entry.ToTimeLocal,entry.SlotsNeeded,court,venue,hours.OpenTime)){var end=start.AddMinutes(entry.SlotsNeeded*court.SlotMinutes);if(await writer.CheckAsync(court,venue,start,end,ct)=="FREE"){match=(court,start,end);break;}}if(match!=null)break;
   }
   if(match==null)continue;
   // A fitting head entry requiring approval or at its hold limit blocks later entries.
   // Do not silently jump over that customer; staff can approve or cancel it.
   if(entry.RequiresApproval)break;var customer=await db.Customers.SingleAsync(x=>x.Id==entry.CustomerId,ct);await writer.LockAsync("customer-holds:"+customer.Phone,ct);
   var activeHolds=await db.Bookings.CountAsync(x=>x.Status==BookingStatus.HELD&&x.HoldExpiresAt>clock.UtcNow&&db.Customers.Any(c=>c.Id==x.CustomerId&&(c.Phone==customer.Phone||(customer.UserId!=null&&c.UserId==customer.UserId))),ct);if(activeHolds>=2)break;
   var candidate=match.Value;await writer.LockAsync("court:"+candidate.Court.Id,ct);if(await writer.CheckAsync(candidate.Court,venue,candidate.Start,candidate.End,ct)!="FREE")break;
   await writer.ExpireStaleAsync(candidate.Court.Id,candidate.Start,candidate.End,events,ct);var price=(await writer.PriceAsync(candidate.Court,venue,customer,candidate.Start,candidate.End,true,ct)).Total;var booking=writer.New(candidate.Court,venue,customer,candidate.Start,candidate.End,price,null,true);booking.HoldExpiresAt=clock.UtcNow.AddMinutes(venue.WaitlistOfferMinutes);if(booking.HoldExpiresAt>candidate.Start)booking.HoldExpiresAt=candidate.Start;db.Bookings.Add(booking);entry.Offer(booking.Id,booking.HoldExpiresAt.Value);writer.Audit(venue.OrganizationId,null,"WAITLIST_OFFERED","WaitlistEntry",entry.Id,new{bookingId=booking.Id});await db.SaveChangesAsync(ct);
   if(notifications!=null)await notifications.QueueWaitlistOfferAsync(booking,entry.Id,publicBaseUrl,ct);events.Add(SchedulingWriter.Event("SlotHeld",booking));
  }
  await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);foreach(var message in events)await notifier.PublishAsync(message,ct);
 }
 public async Task CleanupAsync(CancellationToken ct)
 {
  var venues=await db.Venues.ToListAsync(ct);foreach(var venue in venues){await using var tx=await db.Database.BeginTransactionAsync(ct);await writer.LockAsync("waitlist-venue:"+venue.Id,ct);var today=SeriesCalendar.Today(clock.UtcNow,venue.Timezone);var expired=await db.WaitlistEntries.Where(x=>x.VenueId==venue.Id&&x.Date<today).ToListAsync(ct);foreach(var entry in expired)writer.Audit(entry.OrganizationId,null,"WAITLIST_DATE_EXPIRED","WaitlistEntry",entry.Id,new{entry.Date});db.WaitlistEntries.RemoveRange(expired);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);}
 }
}
