using Futsal.Application.Abstractions;
using Futsal.Application.Scheduling;
using Futsal.Contracts;
using Futsal.Api.Identity;
using Futsal.Domain.Bookings;
using Futsal.Domain.Locations;
using Futsal.Domain.Scheduling;
using Futsal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace Futsal.Api.Scheduling;
public sealed partial class RecurringService(FutsalDbContext db,ICurrentUser current,IClock clock,SchedulingWriter writer,IBookingNotifier notifier)
{
 Guid Actor=>current.UserId??throw new UnauthorizedAccessException();
 static ApiException Invalid(string code="VALIDATION_FAILED")=>new(400,code,code);
 async Task<(Court Court,Venue Venue,Customer Customer)> AccessAsync(SeriesWrite input,CancellationToken ct)
 {
  var court=await db.Courts.SingleOrDefaultAsync(x=>x.Id==input.CourtId,ct)??throw new ApiException(404,"NOT_FOUND","NOT_FOUND");var venue=await db.Venues.SingleAsync(x=>x.Id==court.VenueId,ct);var customer=await db.Customers.SingleOrDefaultAsync(x=>x.Id==input.CustomerId&&x.OrganizationId==venue.OrganizationId,ct)??throw new ApiException(404,"NOT_FOUND","NOT_FOUND");var today=SeriesCalendar.Today(clock.UtcNow,venue.Timezone);
  if(input.Weekday is <0 or >6||input.DurationSlots is <1 or >8||input.StartDate<today.AddYears(-1)||input.StartDate>today.AddYears(2)||input.EndDate<input.StartDate||input.PriceMode is not ("LOCKED" or "CURRENT")||input.Notes?.Length>1000)throw Invalid();return(court,venue,customer);
 }
 public async Task<List<SeriesDatePreview>> PreviewAsync(SeriesWrite input,CancellationToken ct)
 {
  var (court,venue,customer)=await AccessAsync(input,ct);return await PreviewCore(input,court,venue,customer,ct);
 }
 async Task<List<SeriesDatePreview>> PreviewCore(SeriesWrite input,Court court,Venue venue,Customer customer,CancellationToken ct)
 {
  var today=SeriesCalendar.Today(clock.UtcNow,venue.Timezone);var horizon=today.AddDays(7*venue.RecurringHorizonWeeks);var result=new List<SeriesDatePreview>();decimal? locked=null;
  if(input.PriceMode=="LOCKED"){var first=SeriesCalendar.Dates(input.StartDate>today?input.StartDate:today,null,input.Weekday,horizon).FirstOrDefault();if(first!=default){var at=SeriesCalendar.Utc(first,input.StartTimeLocal,venue.Timezone);locked=(await writer.PriceAsync(court,venue,customer,at,at.AddMinutes(input.DurationSlots*court.SlotMinutes),input.ApplyRegularDiscount,ct)).Total;}}
  foreach(var date in SeriesCalendar.Dates(input.StartDate,input.EndDate,input.Weekday,horizon)){var start=SeriesCalendar.Utc(date,input.StartTimeLocal,venue.Timezone);var end=start.AddMinutes(input.DurationSlots*court.SlotMinutes);var code=await writer.CheckAsync(court,venue,start,end,ct);var price=locked??(code=="FREE"?(await writer.PriceAsync(court,venue,customer,start,end,input.ApplyRegularDiscount,ct)).Total:0);result.Add(new(date,start,end,code,price));}return result;
 }
 public async Task<SeriesCreateResult> CreateAsync(SeriesCreate input,string key,CancellationToken ct)
 {
  var events=new List<BookingEvent>();await using var tx=await db.Database.BeginTransactionAsync(ct);var endpoint="POST:/recurring";var replay=await writer.ReplayAsync<SeriesCreateResult>(Actor,key,endpoint,input,ct);if(replay!=null){await tx.CommitAsync(ct);return replay;}
  var (court,venue,customer)=await AccessAsync(input.Series,ct);await Security.SubscriptionGuard.RequireWritableAsync(db,clock,venue.OrganizationId,ct);await writer.LockAsync("court:"+court.Id,ct);var preview=await PreviewCore(input.Series,court,venue,customer,ct);
  if(preview.Count==0)throw Invalid("NO_OCCURRENCES");
  if(!input.FreeDatesOnly&&preview.Any(x=>x.Result!="FREE")){await tx.CommitAsync(ct);return new(null,preview);}
  var d=input.Series;var series=new RecurringBookingSeries{OrganizationId=venue.OrganizationId,VenueId=venue.Id,CourtId=court.Id,CustomerId=customer.Id,Weekday=d.Weekday,StartTimeLocal=d.StartTimeLocal,DurationSlots=d.DurationSlots,StartDate=d.StartDate,EndDate=d.EndDate,PriceMode=d.PriceMode,ApplyRegularDiscount=d.ApplyRegularDiscount,LockedPricePerOccurrence=d.PriceMode=="LOCKED"?(preview.FirstOrDefault(x=>x.Result=="FREE")??preview[0]).Price:null,CreatedBy=Actor,CreatedAt=clock.UtcNow,Notes=d.Notes};db.RecurringSeries.Add(series);db.SeriesRevisions.Add(new(){OrganizationId=series.OrganizationId,VenueId=series.VenueId,SeriesId=series.Id,FromDate=series.StartDate,CourtId=series.CourtId,StartTimeLocal=series.StartTimeLocal,DurationSlots=series.DurationSlots});await db.SaveChangesAsync(ct);
  foreach(var p in preview)
  {
   if(p.Result!="FREE"){await ConflictAsync(series,p.Date,p.Result,ct);continue;}
   await writer.ExpireStaleAsync(court.Id,p.StartUtc,p.EndUtc,events,ct);var b=writer.New(court,venue,customer,p.StartUtc,p.EndUtc,p.Price,Actor);b.SeriesId=series.Id;b.SeriesDate=p.Date;b.Notes=d.Notes;db.Bookings.Add(b);writer.Audit(series.OrganizationId,Actor,"RECURRING_OCCURRENCE_CREATED","Booking",b.Id,new{seriesId=series.Id,date=p.Date});await db.SaveChangesAsync(ct);events.Add(SchedulingWriter.Event("SlotBooked",b));
  }
  var response=new SeriesCreateResult(series.Id,preview);writer.Audit(series.OrganizationId,Actor,"RECURRING_SERIES_CREATED","RecurringSeries",series.Id,new{input.FreeDatesOnly});writer.Remember(Actor,key,endpoint,input,response);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);await Publish(events,ct);return response;
 }
 async Task ConflictAsync(RecurringBookingSeries series,DateOnly date,string reason,CancellationToken ct)
 {
  var conflict=await db.SeriesConflicts.SingleOrDefaultAsync(x=>x.SeriesId==series.Id&&x.Date==date,ct);if(conflict==null){conflict=new(){OrganizationId=series.OrganizationId,VenueId=series.VenueId,SeriesId=series.Id,Date=date,CreatedAt=clock.UtcNow};db.SeriesConflicts.Add(conflict);writer.Audit(series.OrganizationId,null,"RECURRING_CONFLICT","RecurringSeries",series.Id,new{date,reason});}conflict.Reason=reason;conflict.ResolvedAt=reason=="PAST"?clock.UtcNow:null;await db.SaveChangesAsync(ct);
 }
 async Task Publish(List<BookingEvent> events,CancellationToken ct){foreach(var message in events)await notifier.PublishAsync(message,ct);}
 public async Task GenerateAsync(Guid id,CancellationToken ct)
 {
  var events=new List<BookingEvent>();await using var tx=await db.Database.BeginTransactionAsync(ct);await writer.LockAsync("series:"+id,ct);var series=await db.RecurringSeries.SingleOrDefaultAsync(x=>x.Id==id,ct);if(series==null||series.Status is not ("ACTIVE" or "ENDED")){await tx.CommitAsync(ct);return;}var venue=await db.Venues.SingleAsync(x=>x.Id==series.VenueId,ct);await Security.SubscriptionGuard.RequireWritableAsync(db,clock,series.OrganizationId,ct);var today=SeriesCalendar.Today(clock.UtcNow,venue.Timezone);if(series.Status=="ENDED"&&series.EndDate<today){await tx.CommitAsync(ct);return;}foreach(var c in await db.SeriesConflicts.Where(x=>x.SeriesId==id&&x.Date<today&&x.ResolvedAt==null).ToListAsync(ct))c.ResolvedAt=clock.UtcNow;await db.SaveChangesAsync(ct);var revisions=await db.SeriesRevisions.Where(x=>x.SeriesId==id).OrderBy(x=>x.FromDate).ToListAsync(ct);foreach(var court in revisions.Select(x=>x.CourtId).Distinct().Order())await writer.LockAsync("court:"+court,ct);var customer=await db.Customers.SingleAsync(x=>x.Id==series.CustomerId,ct);
  if(series.PriceMode=="CURRENT")
  {
   var future=await db.Bookings.Where(x=>x.SeriesId==id&&x.Status==BookingStatus.CONFIRMED&&x.StartAt>clock.UtcNow).ToListAsync(ct);
   foreach(var b in future)
   {
    var court=await db.Courts.SingleAsync(x=>x.Id==b.CourtId,ct);var price=await writer.PriceAsync(court,venue,customer,b.StartAt,b.EndAt,series.ApplyRegularDiscount,ct);if(price.Total==b.TotalAmount){var stale=await db.SeriesConflicts.SingleOrDefaultAsync(x=>x.SeriesId==id&&x.Date==b.SeriesDate&&x.Reason=="PAID_EXCEEDS_NEW_PRICE"&&x.ResolvedAt==null,ct);if(stale!=null&&price.Total>=b.PaidAmount){stale.ResolvedAt=clock.UtcNow;await db.SaveChangesAsync(ct);}continue;}
    if(price.Total<b.PaidAmount){await ConflictAsync(series,b.SeriesDate!.Value,"PAID_EXCEEDS_NEW_PRICE",ct);continue;}
    b.TotalAmount=price.Total;b.DiscountAmount=price.Discount;b.AdvanceRequired=venue.AdvanceType=="FIXED"?Math.Min(price.Total,venue.AdvanceValue):decimal.Round(price.Total*venue.AdvanceValue/100m,2);b.UpdatedAt=clock.UtcNow;writer.Audit(series.OrganizationId,null,"RECURRING_PRICE_UPDATED","Booking",b.Id,new{price.Total});var conflict=await db.SeriesConflicts.SingleOrDefaultAsync(x=>x.SeriesId==id&&x.Date==b.SeriesDate&&x.Reason=="PAID_EXCEEDS_NEW_PRICE",ct);if(conflict!=null)conflict.ResolvedAt=clock.UtcNow;await db.SaveChangesAsync(ct);events.Add(SchedulingWriter.Event("SlotBooked",b));
   }
  }
  foreach(var date in SeriesCalendar.Dates(series.StartDate>today?series.StartDate:today,series.EndDate,series.Weekday,today.AddDays(venue.RecurringHorizonWeeks*7)))
  {
   if(await db.Bookings.AnyAsync(x=>x.SeriesId==id&&x.SeriesDate==date,ct))continue;
   var exception=await db.SeriesExceptions.SingleOrDefaultAsync(x=>x.SeriesId==id&&x.Date==date,ct);if(exception?.Type=="SKIPPED")continue;var revision=revisions.Last(x=>x.FromDate<=date);var court=await db.Courts.SingleAsync(x=>x.Id==revision.CourtId,ct);var start=exception?.MovedToStartUtc??SeriesCalendar.Utc(date,revision.StartTimeLocal,venue.Timezone);var end=start.AddMinutes(revision.DurationSlots*court.SlotMinutes);var reason=await writer.CheckAsync(court,venue,start,end,ct);
   if(reason!="FREE"){await ConflictAsync(series,date,reason,ct);continue;}
   await writer.ExpireStaleAsync(court.Id,start,end,events,ct);var price=series.LockedPricePerOccurrence??(await writer.PriceAsync(court,venue,customer,start,end,series.ApplyRegularDiscount,ct)).Total;var b=writer.New(court,venue,customer,start,end,price,series.CreatedBy);b.SeriesId=id;b.SeriesDate=date;b.Notes=series.Notes;
   await tx.CreateSavepointAsync("occurrence",ct);var previous=db.ChangeTracker.Entries().Select(x=>x.Entity).ToHashSet();
   try{db.Bookings.Add(b);writer.Audit(series.OrganizationId,null,"RECURRING_OCCURRENCE_CREATED","Booking",b.Id,new{seriesId=id,date});await db.SaveChangesAsync(ct);var old=await db.SeriesConflicts.SingleOrDefaultAsync(x=>x.SeriesId==id&&x.Date==date,ct);if(old!=null)old.ResolvedAt=clock.UtcNow;await db.SaveChangesAsync(ct);events.Add(SchedulingWriter.Event("SlotBooked",b));await tx.ReleaseSavepointAsync("occurrence",ct);}
   catch(Exception ex)when(SchedulingWriter.Overlap(ex)){await tx.RollbackToSavepointAsync("occurrence",ct);foreach(var e in db.ChangeTracker.Entries().Where(e=>!previous.Contains(e.Entity)).ToArray())e.State=EntityState.Detached;await ConflictAsync(series,date,"BOOKED",ct);}
  }
  if(series.EndDate<today){series.End(series.EndDate.Value);writer.Audit(series.OrganizationId,null,"RECURRING_SERIES_ENDED","RecurringSeries",id);await db.SaveChangesAsync(ct);}await tx.CommitAsync(ct);await Publish(events,ct);
 }
 public async Task<List<SeriesListItem>> ListAsync(Guid? venueId,string? status,CancellationToken ct,Guid? id=null)
 {
  return await db.RecurringSeries.Where(x=>(id==null||x.Id==id)&&(venueId==null||x.VenueId==venueId)&&(status==null||x.Status==status)).OrderByDescending(x=>x.CreatedAt).Select(s=>new SeriesListItem(s.Id,s.VenueId,s.CourtId,s.CustomerId,db.Customers.Where(c=>c.Id==s.CustomerId).Select(c=>c.FullName).First(),db.Courts.Where(c=>c.Id==s.CourtId).Select(c=>c.Name).First(),s.Weekday,s.StartTimeLocal,s.DurationSlots,s.StartDate,s.EndDate,s.Status,s.PriceMode,s.LockedPricePerOccurrence,db.SeriesConflicts.Count(c=>c.SeriesId==s.Id&&c.ResolvedAt==null),s.ApplyRegularDiscount,s.Notes)).Take(200).ToListAsync(ct);
 }
 public async Task<SeriesDetail> DetailAsync(Guid id,CancellationToken ct)
 {
  var item=(await ListAsync(null,null,ct,id)).SingleOrDefault(x=>x.Id==id)??throw new ApiException(404,"NOT_FOUND","NOT_FOUND");var bookings=await db.Bookings.Where(x=>x.SeriesId==id).OrderBy(x=>x.SeriesDate).ToListAsync(ct);var exceptions=await db.SeriesExceptions.Where(x=>x.SeriesId==id).ToListAsync(ct);var conflicts=await db.SeriesConflicts.Where(x=>x.SeriesId==id).ToListAsync(ct);var dates=bookings.Select(x=>x.SeriesDate!.Value).Concat(exceptions.Select(x=>x.Date)).Concat(conflicts.Select(x=>x.Date)).Distinct().Order();var list=new List<SeriesOccurrence>();foreach(var date in dates){var b=bookings.LastOrDefault(x=>x.SeriesDate==date);var e=exceptions.SingleOrDefault(x=>x.Date==date);var c=conflicts.SingleOrDefault(x=>x.Date==date);var state=e?.Type=="SKIPPED"?"SKIPPED":b!=null&&b.Status!=BookingStatus.CONFIRMED?b.Status.ToString():e?.Type=="MOVED"?"MOVED":b?.Status.ToString()??(c?.Reason=="PAST"?"SKIPPED":"CONFLICT");list.Add(new(b?.Id,date,b?.StartAt,b?.EndAt,state,b?.TotalAmount??0,b?.PaidAmount??0,e?.Reason??(b==null||c?.ResolvedAt==null?c?.Reason:null)));}
  var allocations=await (from p in db.Payments join b in db.Bookings on p.BookingId equals b.Id where b.SeriesId==id&&p.PrepaymentBlockId!=null orderby b.StartAt,p.CreatedAt select new PrepaymentAllocation(p.PrepaymentBlockId!.Value,p.Id,b.Id,b.StartAt,p.Amount,p.Status.ToString())).ToListAsync(ct);return new(item,list,allocations);
 }
}
