using Futsal.Application.Abstractions;
using Futsal.Application.Scheduling;
using Futsal.Contracts;
using Futsal.Api.Identity;
using Futsal.Api.Bookings;
using Futsal.Domain.Bookings;
using Futsal.Domain.Identity;
using Futsal.Domain.Locations;
using Futsal.Domain.Scheduling;
using Futsal.Infrastructure.Notifications;
using Futsal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace Futsal.Api.Scheduling;
public sealed partial class WaitlistService(FutsalDbContext db,ICurrentUser current,IClock clock,SchedulingWriter writer,IBookingNotifier notifier,NotificationQueue? notifications=null,string? publicBaseUrl=null)
{
 Guid Actor=>current.UserId??throw new UnauthorizedAccessException();
 async Task<AppUser> Player(CancellationToken ct){if(current.Role!="Customer"||!current.IsAuthenticated)throw new ApiException(403,"FORBIDDEN","FORBIDDEN");return await db.Users.SingleOrDefaultAsync(x=>x.Id==Actor&&x.IsActive&&x.Phone!=null,ct)??throw new ApiException(403,"FORBIDDEN","FORBIDDEN");}
 public async Task<WaitlistView> JoinAsync(WaitlistJoin input,string key,bool staff,CancellationToken ct)
 {
  await using var tx=await db.Database.BeginTransactionAsync(ct);var endpoint="POST:/waitlist/"+(staff?"staff":"customer");var replay=await writer.ReplayAsync<WaitlistView>(Actor,key,endpoint,input,ct);if(replay!=null){await tx.CommitAsync(ct);return replay;}
  var venue=staff?await db.Venues.SingleOrDefaultAsync(x=>x.Id==input.VenueId,ct):await db.Venues.IgnoreQueryFilters().SingleOrDefaultAsync(x=>x.Id==input.VenueId&&x.IsPublic&&x.IsActive,ct);if(venue==null)throw new ApiException(404,"NOT_FOUND","NOT_FOUND");await writer.LockAsync("waitlist-venue:"+venue.Id,ct);await Security.SubscriptionGuard.RequireWritableAsync(db,clock,venue.OrganizationId,ct);
  var today=SeriesCalendar.Today(clock.UtcNow,venue.Timezone);if(input.Date<today||input.Date>today.AddDays(365)||input.SlotsNeeded is <1 or >8||input.FromTimeLocal>=input.ToTimeLocal)throw new ApiException(400,"VALIDATION_FAILED","VALIDATION_FAILED");
  Customer customer;
  if(staff){customer=await db.Customers.SingleOrDefaultAsync(x=>x.Id==input.CustomerId&&x.OrganizationId==venue.OrganizationId,ct)??throw new ApiException(404,"NOT_FOUND","NOT_FOUND");}
  else
  {
   if(input.CustomerId!=null)throw new ApiException(400,"VALIDATION_FAILED","VALIDATION_FAILED");var player=await Player(ct);var phone=NepaliPhone.Normalize(player.Phone!);await writer.LockAsync("customer-holds:"+phone,ct);customer=await db.Customers.IgnoreQueryFilters().SingleOrDefaultAsync(x=>x.OrganizationId==venue.OrganizationId&&x.Phone==phone,ct)??new(){OrganizationId=venue.OrganizationId,Phone=phone,FullName=player.FullName,CreatedAt=clock.UtcNow};customer.UserId=player.Id;if(db.Entry(customer).State==EntityState.Detached)db.Customers.Add(customer);
  }
  if(!Futsal.Application.Notifications.SmsText.ValidPhone(customer.Phone))throw new ApiException(400,"INVALID_PHONE","INVALID_PHONE");
  await writer.LockAsync("waitlist-customer:"+customer.Phone,ct);
  if(await db.WaitlistEntries.IgnoreQueryFilters().AnyAsync(x=>x.CustomerId==customer.Id&&x.VenueId==venue.Id&&x.Date==input.Date&&x.CourtId==input.CourtId&&x.FromTimeLocal==input.FromTimeLocal&&x.ToTimeLocal==input.ToTimeLocal&&x.SlotsNeeded==input.SlotsNeeded&&(x.Status=="WAITING"||x.Status=="OFFERED"),ct))throw new ApiException(409,"WAITLIST_DUPLICATE","WAITLIST_DUPLICATE");
  var count=await db.WaitlistEntries.IgnoreQueryFilters().CountAsync(x=>(x.Status=="WAITING"||x.Status=="OFFERED")&&x.Date>=today&&db.Customers.IgnoreQueryFilters().Any(c=>c.Id==x.CustomerId&&(c.Phone==customer.Phone||(customer.UserId!=null&&c.UserId==customer.UserId))),ct);if(count>=3)throw new ApiException(409,"TOO_MANY_WAITLIST_ENTRIES","TOO_MANY_WAITLIST_ENTRIES");
  var courts=staff?await db.Courts.Where(x=>x.VenueId==venue.Id&&(input.CourtId==null||x.Id==input.CourtId)).ToListAsync(ct):await db.Courts.IgnoreQueryFilters().Where(x=>x.VenueId==venue.Id&&x.IsActive&&(input.CourtId==null||x.Id==input.CourtId)).ToListAsync(ct);if(courts.Count==0)throw new ApiException(404,"NOT_FOUND","NOT_FOUND");bool taken=false;
  foreach(var court in courts)
  {
   var hours=await db.CourtHours.IgnoreQueryFilters().SingleOrDefaultAsync(x=>x.CourtId==court.Id&&x.Weekday==(short)input.Date.DayOfWeek,ct);if(hours==null)continue;
   foreach(var start in Candidates(input.Date,input.FromTimeLocal,input.ToTimeLocal,input.SlotsNeeded,court,venue,hours.OpenTime))
   {
   var code=await writer.CheckAsync(court,venue,start,start.AddMinutes(input.SlotsNeeded*court.SlotMinutes),ct);if(code=="FREE")throw new ApiException(409,"SLOT_ALREADY_AVAILABLE","SLOT_ALREADY_AVAILABLE");if(code=="BOOKED")taken=true;
   }
  }
  if(!taken)throw new ApiException(409,"WAITLIST_REQUIRES_TAKEN_SLOT","WAITLIST_REQUIRES_TAKEN_SLOT");
  var entry=new WaitlistEntry{OrganizationId=venue.OrganizationId,VenueId=venue.Id,CourtId=input.CourtId,CustomerId=customer.Id,Date=input.Date,FromTimeLocal=input.FromTimeLocal,ToTimeLocal=input.ToTimeLocal,SlotsNeeded=input.SlotsNeeded,CreatedAt=clock.UtcNow,RequiresApproval=customer.NoShowCount>venue.WaitlistNoShowThreshold};db.WaitlistEntries.Add(entry);writer.Audit(venue.OrganizationId,Actor,"WAITLIST_JOINED","WaitlistEntry",entry.Id);await db.SaveChangesAsync(ct);var response=await ViewAsync(entry,staff,ct);writer.Remember(Actor,key,endpoint,input,response);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return response;
 }
 static IEnumerable<DateTimeOffset> Candidates(DateOnly date,TimeOnly from,TimeOnly to,int slots,Court court,Venue venue,TimeOnly? open=null)
 {
  if(court.SlotMinutes<=0)yield break;var grid=open??TimeOnly.MinValue;var minutes=(from.ToTimeSpan()-grid.ToTimeSpan()).TotalMinutes;var offset=Math.Max(0,Math.Ceiling(minutes/court.SlotMinutes))*court.SlotMinutes;if(grid.ToTimeSpan().TotalMinutes+offset>=1440)yield break;var aligned=grid.AddMinutes(offset);var start=SeriesCalendar.Utc(date,aligned,venue.Timezone);var end=SeriesCalendar.Utc(date,to,venue.Timezone);for(var time=start;time.AddMinutes(slots*court.SlotMinutes)<=end;time=time.AddMinutes(court.SlotMinutes))yield return time;
 }
 async Task<WaitlistView> ViewAsync(WaitlistEntry e,bool staff,CancellationToken ct)
 {
  var venue=await db.Venues.IgnoreQueryFilters().SingleAsync(x=>x.Id==e.VenueId,ct);var court=e.CourtId==null?null:await db.Courts.IgnoreQueryFilters().Where(x=>x.Id==e.CourtId).Select(x=>x.Name).SingleOrDefaultAsync(ct);var customer=staff?await db.Customers.IgnoreQueryFilters().SingleAsync(x=>x.Id==e.CustomerId,ct):null;return new(e.Id,e.VenueId,venue.Name,venue.Slug,e.CourtId,court,e.Date,e.FromTimeLocal,e.ToTimeLocal,e.SlotsNeeded,e.Status,e.Position,e.RequiresApproval,e.OfferedBookingId,e.OfferExpiresAt,customer?.Id,customer?.FullName,customer?.Phone);
 }
 async Task<WaitlistEntry> OwnedAsync(Guid id,bool staff,CancellationToken ct)
 {
  if(staff)return await db.WaitlistEntries.SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new ApiException(404,"NOT_FOUND","NOT_FOUND");
  var player=await Player(ct);var phone=NepaliPhone.Normalize(player.Phone!);return await db.WaitlistEntries.IgnoreQueryFilters().SingleOrDefaultAsync(x=>x.Id==id&&db.Customers.IgnoreQueryFilters().Any(c=>c.Id==x.CustomerId&&(c.UserId==player.Id||c.Phone==phone)),ct)??throw new ApiException(404,"NOT_FOUND","NOT_FOUND");
 }
 public async Task<List<WaitlistView>> ListAsync(Guid? venueId,DateOnly? date,bool staff,CancellationToken ct)
 {
  List<WaitlistEntry> entries;if(staff)entries=await db.WaitlistEntries.Where(x=>(venueId==null||x.VenueId==venueId)&&(date==null||x.Date==date)).OrderBy(x=>x.Position).Take(200).ToListAsync(ct);else{var player=await Player(ct);var phone=NepaliPhone.Normalize(player.Phone!);entries=await db.WaitlistEntries.IgnoreQueryFilters().Where(x=>db.Customers.IgnoreQueryFilters().Any(c=>c.Id==x.CustomerId&&(c.UserId==player.Id||c.Phone==phone))).OrderBy(x=>x.Date).Take(100).ToListAsync(ct);}var result=new List<WaitlistView>();foreach(var e in entries)result.Add(await ViewAsync(e,staff,ct));return result;
 }
 public async Task<BookingDto> ClaimAsync(Guid id,CancellationToken ct)
 {
  await using var tx=await db.Database.BeginTransactionAsync(ct);var entry=await OwnedAsync(id,false,ct);var player=await Player(ct);await writer.LockAsync("waitlist-venue:"+entry.VenueId,ct);await db.Entry(entry).ReloadAsync(ct);var customer=await db.Customers.IgnoreQueryFilters().SingleAsync(x=>x.Id==entry.CustomerId,ct);customer.UserId=player.Id;var booking=await db.Bookings.IgnoreQueryFilters().SingleOrDefaultAsync(x=>x.Id==entry.OfferedBookingId&&x.CustomerId==customer.Id,ct)??throw new ApiException(404,"NOT_FOUND","NOT_FOUND");if(entry.Status is not ("OFFERED" or "CONVERTED")||(booking.Status==BookingStatus.HELD&&booking.HoldExpiresAt<=clock.UtcNow))throw new ApiException(409,"OFFER_EXPIRED","OFFER_EXPIRED");writer.Audit(entry.OrganizationId,Actor,"WAITLIST_OFFER_OPENED","WaitlistEntry",entry.Id);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return new(booking.Id,booking.BookingCode,booking.Status.ToString(),booking.StartAt,booking.EndAt,booking.HoldExpiresAt,booking.TotalAmount,booking.DiscountAmount,booking.AdvanceRequired,booking.PaidAmount);
 }
 public async Task CancelAsync(Guid id,bool staff,CancellationToken ct)
 {
  var events=new List<BookingEvent>();await using var tx=await db.Database.BeginTransactionAsync(ct);var entry=await OwnedAsync(id,staff,ct);await writer.LockAsync("waitlist-venue:"+entry.VenueId,ct);await db.Entry(entry).ReloadAsync(ct);if(entry.OfferedBookingId is Guid bookingId){var booking=await db.Bookings.IgnoreQueryFilters().SingleAsync(x=>x.Id==bookingId,ct);await writer.LockAsync("court:"+booking.CourtId,ct);await db.Entry(booking).ReloadAsync(ct);if(booking.Status==BookingStatus.HELD){booking.Cancel(clock.UtcNow,"Waitlist cancelled");events.Add(SchedulingWriter.Event("SlotReleased",booking));}else if(booking.Status==BookingStatus.CONFIRMED)throw new ApiException(409,"OFFER_ALREADY_CONVERTED","OFFER_ALREADY_CONVERTED");}
  try{entry.Cancel();}catch(ArgumentException ex){throw new ApiException(409,ex.Message,ex.Message);}writer.Audit(entry.OrganizationId,Actor,"WAITLIST_CANCELLED","WaitlistEntry",id);if(!staff){var customer=await db.Customers.IgnoreQueryFilters().SingleAsync(x=>x.Id==entry.CustomerId,ct);customer.UserId=Actor;}await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);foreach(var message in events)await notifier.PublishAsync(message,ct);
 }
 public async Task ApproveAsync(Guid id,CancellationToken ct)
 {
  await using var tx=await db.Database.BeginTransactionAsync(ct);var entry=await OwnedAsync(id,true,ct);await writer.LockAsync("waitlist-venue:"+entry.VenueId,ct);await db.Entry(entry).ReloadAsync(ct);if(entry.Status!="WAITING")throw new ApiException(409,"INVALID_WAITLIST_STATE","INVALID_WAITLIST_STATE");entry.RequiresApproval=false;writer.Audit(entry.OrganizationId,Actor,"WAITLIST_APPROVED","WaitlistEntry",id);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
 }
 public async Task ConfirmStaffAsync(Guid id,WaitlistConfirm input,string key,CancellationToken ct)
 {
  await using var tx=await db.Database.BeginTransactionAsync(ct);var endpoint="POST:/waitlist/"+id+"/confirm";if(await writer.ReplayAsync<object>(Actor,key,endpoint,input,ct)!=null){await tx.CommitAsync(ct);return;}var entry=await OwnedAsync(id,true,ct);await writer.LockAsync("waitlist-venue:"+entry.VenueId,ct);await db.Entry(entry).ReloadAsync(ct);var booking=await db.Bookings.SingleOrDefaultAsync(x=>x.Id==entry.OfferedBookingId,ct)??throw new ApiException(404,"NOT_FOUND","NOT_FOUND");await writer.LockAsync("court:"+booking.CourtId,ct);await db.Entry(booking).ReloadAsync(ct);if(entry.Status!="OFFERED"||booking.Status!=BookingStatus.HELD||booking.HoldExpiresAt<=clock.UtcNow)throw new ApiException(409,"OFFER_EXPIRED","OFFER_EXPIRED");if(input.Amount<0||input.Amount>booking.TotalAmount||decimal.Round(input.Amount,2)!=input.Amount||!Enum.TryParse<PaymentMethod>(input.Method,out var method)||method is not (PaymentMethod.CASH or PaymentMethod.BANK or PaymentMethod.OTHER))throw new ApiException(400,"VALIDATION_FAILED","VALIDATION_FAILED");booking.Confirm();booking.PaidAmount=input.Amount;if(input.Amount>0)db.Payments.Add(new(){OrganizationId=entry.OrganizationId,BookingId=booking.Id,Amount=input.Amount,Method=method,Status=PaymentStatus.COMPLETED,ReceivedBy=Actor,CreatedAt=clock.UtcNow,VerifiedAt=clock.UtcNow});entry.Convert();writer.Audit(entry.OrganizationId,Actor,"WAITLIST_CONVERTED","WaitlistEntry",id);writer.Remember(Actor,key,endpoint,input,new{ok=true});await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);await notifier.PublishAsync(SchedulingWriter.Event("SlotBooked",booking),ct);
 }
}
