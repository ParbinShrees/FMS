using Futsal.Application.Scheduling;
using Futsal.Contracts;
using Futsal.Api.Identity;
using Futsal.Domain.Bookings;
using Futsal.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;
namespace Futsal.Api.Scheduling;
public sealed partial class RecurringService
{
 async Task<RecurringBookingSeries> SeriesAsync(Guid id,CancellationToken ct){await writer.LockAsync("series:"+id,ct);return await db.RecurringSeries.SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new ApiException(404,"NOT_FOUND","NOT_FOUND");}
 public async Task StateAsync(Guid id,string action,SeriesFromDate input,string key,CancellationToken ct)
 {
  if(input.Reason?.Length>1000)throw Invalid();
  var events=new List<Futsal.Application.Abstractions.BookingEvent>();await using var tx=await db.Database.BeginTransactionAsync(ct);var endpoint="POST:/recurring/"+id+"/"+action;var request=new{action,input};if(await writer.ReplayAsync<object>(Actor,key,endpoint,request,ct)!=null){await tx.CommitAsync(ct);return;}var series=await SeriesAsync(id,ct);var venue=await db.Venues.SingleAsync(x=>x.Id==series.VenueId,ct);
  try{switch(action){case "pause":series.Pause();break;case "resume":series.Resume();break;case "end":case "delete":
    if(action=="delete"&&current.Role is not ("Owner" or "Manager")&&!current.IsSuperAdmin)throw new ApiException(403,"FORBIDDEN","FORBIDDEN");var today=SeriesCalendar.Today(clock.UtcNow,venue.Timezone);if(input.Date<today)throw Invalid("PAST_OCCURRENCE");
    var future=await db.Bookings.Where(x=>x.SeriesId==id&&x.SeriesDate>=input.Date&&x.StartAt>clock.UtcNow&&x.Status==BookingStatus.CONFIRMED).ToListAsync(ct);foreach(var court in future.Select(x=>x.CourtId).Distinct().Order())await writer.LockAsync("court:"+court,ct);foreach(var b in future)writer.Cancel(b,venue,input.Reason,Actor,events);series.End(input.Date.AddDays(-1));break;
   default:throw Invalid();}}
  catch(ArgumentException ex){throw Invalid(ex.Message);}
  writer.Audit(series.OrganizationId,Actor,"RECURRING_"+action.ToUpperInvariant(),"RecurringSeries",id,input);writer.Remember(Actor,key,endpoint,request,new{ok=true});await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);await Publish(events,ct);if(action=="resume")await GenerateAsync(id,ct);
 }
 public async Task OccurrenceAsync(Guid id,string action,SeriesDateAction input,string key,CancellationToken ct)
 {
  if(input.Reason?.Length>1000)throw Invalid();
  var events=new List<Futsal.Application.Abstractions.BookingEvent>();await using var tx=await db.Database.BeginTransactionAsync(ct);var endpoint="POST:/recurring/"+id+"/"+action;var request=new{action,input};if(await writer.ReplayAsync<object>(Actor,key,endpoint,request,ct)!=null){await tx.CommitAsync(ct);return;}var series=await SeriesAsync(id,ct);var venue=await db.Venues.SingleAsync(x=>x.Id==series.VenueId,ct);if(input.Date<series.StartDate||series.EndDate<input.Date||(short)input.Date.DayOfWeek!=series.Weekday)throw Invalid("PAST_OCCURRENCE");
  var booking=await db.Bookings.Where(x=>x.SeriesId==id&&x.SeriesDate==input.Date).OrderByDescending(x=>x.CreatedAt).FirstOrDefaultAsync(ct);if(booking!=null&&booking.StartAt<=clock.UtcNow||booking==null&&input.Date<SeriesCalendar.Today(clock.UtcNow,venue.Timezone))throw Invalid("PAST_OCCURRENCE");var revision=await db.SeriesRevisions.Where(x=>x.SeriesId==id&&x.FromDate<=input.Date).OrderByDescending(x=>x.FromDate).FirstAsync(ct);var court=await db.Courts.SingleAsync(x=>x.Id==(booking==null?revision.CourtId:booking.CourtId),ct);await writer.LockAsync("court:"+court.Id,ct);var exception=await db.SeriesExceptions.SingleOrDefaultAsync(x=>x.SeriesId==id&&x.Date==input.Date,ct);
  if(action is "skip" or "cancel")
  {
   if(booking!=null&&booking.Status==BookingStatus.CONFIRMED)writer.Cancel(booking,venue,input.Reason,Actor,events);else if(booking!=null&&booking.Status is not (BookingStatus.CANCELLED or BookingStatus.EXPIRED))throw Invalid("INVALID_SERIES_STATE");
   if(exception==null){exception=new(){OrganizationId=series.OrganizationId,VenueId=series.VenueId,SeriesId=id,Date=input.Date};db.SeriesExceptions.Add(exception);}exception.Type="SKIPPED";exception.MovedToStartUtc=null;exception.Reason=input.Reason;
  }
  else if(action=="move")
  {
   if(input.MovedToStartUtc==null)throw Invalid();var start=input.MovedToStartUtc.Value.ToUniversalTime();var end=start.AddMinutes(court.SlotMinutes*revision.DurationSlots);var check=await writer.CheckAsync(court,venue,start,end,ct,booking?.Id);if(check!="FREE")throw new ApiException(409,"SERIES_CHANGE_CONFLICT",check);await writer.ExpireStaleAsync(court.Id,start,end,events,ct);
   if(booking==null){var customer=await db.Customers.SingleAsync(x=>x.Id==series.CustomerId,ct);var amount=series.LockedPricePerOccurrence??(await writer.PriceAsync(court,venue,customer,start,end,series.ApplyRegularDiscount,ct)).Total;booking=writer.New(court,venue,customer,start,end,amount,Actor);booking.SeriesId=id;booking.SeriesDate=input.Date;db.Bookings.Add(booking);}else{if(booking.Status!=BookingStatus.CONFIRMED)throw Invalid("INVALID_SERIES_STATE");var customer=await db.Customers.SingleAsync(x=>x.Id==series.CustomerId,ct);var newPrice=series.LockedPricePerOccurrence??(await writer.PriceAsync(court,venue,customer,start,end,series.ApplyRegularDiscount,ct)).Total;if(booking.PaidAmount>newPrice)throw Invalid("PAID_EXCEEDS_NEW_PRICE");events.Add(SchedulingWriter.Event("SlotReleased",booking));booking.Reschedule(start,end,clock.UtcNow);booking.TotalAmount=newPrice;booking.AdvanceRequired=venue.AdvanceType=="FIXED"?Math.Min(newPrice,venue.AdvanceValue):decimal.Round(newPrice*venue.AdvanceValue/100m,2);}
   if(exception==null){exception=new(){OrganizationId=series.OrganizationId,VenueId=series.VenueId,SeriesId=id,Date=input.Date};db.SeriesExceptions.Add(exception);}exception.Type="MOVED";exception.MovedToStartUtc=start;exception.Reason=input.Reason;events.Add(SchedulingWriter.Event("SlotBooked",booking));
  }
  else throw Invalid();
  var conflict=await db.SeriesConflicts.SingleOrDefaultAsync(x=>x.SeriesId==id&&x.Date==input.Date,ct);if(conflict!=null)conflict.ResolvedAt=clock.UtcNow;writer.Audit(series.OrganizationId,Actor,"RECURRING_DATE_"+action.ToUpperInvariant(),"RecurringSeries",id,input);writer.Remember(Actor,key,endpoint,request,new{ok=true});await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);await Publish(events,ct);
 }
 public async Task<List<SeriesDatePreview>> ChangeAsync(Guid id,SeriesChange input,string key,CancellationToken ct)
 {
  var events=new List<Futsal.Application.Abstractions.BookingEvent>();await using var tx=await db.Database.BeginTransactionAsync(ct);var endpoint="POST:/recurring/"+id+"/change";var replay=await writer.ReplayAsync<List<SeriesDatePreview>>(Actor,key,endpoint,input,ct);if(replay!=null){await tx.CommitAsync(ct);return replay;}var series=await SeriesAsync(id,ct);var venue=await db.Venues.SingleAsync(x=>x.Id==series.VenueId,ct);var today=SeriesCalendar.Today(clock.UtcNow,venue.Timezone);if(input.FromDate<today||input.FromDate>today.AddYears(2)||series.EndDate<input.FromDate||input.DurationSlots is <1 or >8)throw Invalid();var court=await db.Courts.SingleOrDefaultAsync(x=>x.Id==input.CourtId&&x.VenueId==venue.Id,ct)??throw new ApiException(404,"NOT_FOUND","NOT_FOUND");
  var future=await db.Bookings.Where(x=>x.SeriesId==id&&x.SeriesDate>=input.FromDate&&x.StartAt>clock.UtcNow&&x.Status==BookingStatus.CONFIRMED).ToListAsync(ct);foreach(var courtId in future.Select(x=>x.CourtId).Append(court.Id).Distinct().Order())await writer.LockAsync("court:"+courtId,ct);var customer=await db.Customers.SingleAsync(x=>x.Id==series.CustomerId,ct);
  // Vacate this series' future ranges inside the transaction only. The unchanged exclusion
  // constraint judges every re-placement; readers still see the pre-commit schedule.
  var ids=future.Select(x=>x.Id).ToArray();await db.Bookings.Where(x=>ids.Contains(x.Id)).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,BookingStatus.CANCELLED),ct);
  var report=new List<SeriesDatePreview>();var exceptions=await db.SeriesExceptions.Where(x=>x.SeriesId==id).ToListAsync(ct);
  var dates=SeriesCalendar.Dates(input.FromDate>series.StartDate?input.FromDate:series.StartDate,series.EndDate,series.Weekday,today.AddDays(venue.RecurringHorizonWeeks*7)).Concat(future.Select(b=>b.SeriesDate!.Value)).Distinct().Order();
  foreach(var date in dates)
  {
   var ex=exceptions.SingleOrDefault(x=>x.Date==date);if(ex?.Type=="SKIPPED")continue;var start=ex?.MovedToStartUtc??SeriesCalendar.Utc(date,input.StartTimeLocal,venue.Timezone);var end=start.AddMinutes(court.SlotMinutes*input.DurationSlots);var old=future.SingleOrDefault(x=>x.SeriesDate==date);if(old==null&&await db.Bookings.AnyAsync(x=>x.SeriesId==id&&x.SeriesDate==date,ct))continue;var check=await writer.CheckAsync(court,venue,start,end,ct);var amount=series.LockedPricePerOccurrence??(await writer.PriceAsync(court,venue,customer,start,end,series.ApplyRegularDiscount,ct)).Total;if(old?.PaidAmount>amount)check="PAID_EXCEEDS_NEW_PRICE";if(report.Any(r=>r.StartUtc<end&&r.EndUtc>start))check="BOOKED";report.Add(new(date,start,end,check,amount));
  }
  if(report.Any(x=>x.Result!="FREE")){await tx.RollbackAsync(ct);db.ChangeTracker.Clear();return report;}
  foreach(var row in report)
  {
   await writer.ExpireStaleAsync(court.Id,row.StartUtc,row.EndUtc,events,ct);var b=future.SingleOrDefault(x=>x.SeriesDate==row.Date);if(b==null){b=writer.New(court,venue,customer,row.StartUtc,row.EndUtc,row.Price,Actor);b.SeriesId=id;b.SeriesDate=row.Date;db.Bookings.Add(b);}else{events.Add(SchedulingWriter.Event("SlotReleased",b));b.Reschedule(row.StartUtc,row.EndUtc,clock.UtcNow);b.CourtId=court.Id;b.TotalAmount=row.Price;b.AdvanceRequired=venue.AdvanceType=="FIXED"?Math.Min(row.Price,venue.AdvanceValue):decimal.Round(row.Price*venue.AdvanceValue/100m,2);db.Entry(b).Property(x=>x.Status).IsModified=true;}
   // SQL vacating changes xmin; reload just the concurrency token before guarded restoration.
   if(db.Entry(b).State!=EntityState.Added){var values=await db.Entry(b).GetDatabaseValuesAsync(ct);db.Entry(b).Property("xmin").OriginalValue=values!["xmin"]!;}
   await db.SaveChangesAsync(ct);events.Add(SchedulingWriter.Event("SlotBooked",b));
  }
  foreach(var c in await db.SeriesConflicts.Where(x=>x.SeriesId==id&&x.Date>=input.FromDate).ToListAsync(ct))if(report.Any(r=>r.Date==c.Date))c.ResolvedAt=clock.UtcNow;
  db.SeriesRevisions.RemoveRange(await db.SeriesRevisions.Where(x=>x.SeriesId==id&&x.FromDate>input.FromDate).ToListAsync(ct));
  var revision=await db.SeriesRevisions.SingleOrDefaultAsync(x=>x.SeriesId==id&&x.FromDate==input.FromDate,ct);if(revision==null){revision=new(){OrganizationId=series.OrganizationId,VenueId=series.VenueId,SeriesId=id,FromDate=input.FromDate};db.SeriesRevisions.Add(revision);}revision.CourtId=input.CourtId;revision.StartTimeLocal=input.StartTimeLocal;revision.DurationSlots=input.DurationSlots;series.CourtId=court.Id;series.StartTimeLocal=input.StartTimeLocal;series.DurationSlots=input.DurationSlots;writer.Audit(series.OrganizationId,Actor,"RECURRING_SERIES_CHANGED","RecurringSeries",id,input);writer.Remember(Actor,key,endpoint,input,report);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);await Publish(events,ct);return report;
 }
 public async Task<List<PrepaymentAllocation>> PrepayAsync(Guid id,SeriesPrepay input,string key,CancellationToken ct)
 {
  var events=new List<Futsal.Application.Abstractions.BookingEvent>();await using var tx=await db.Database.BeginTransactionAsync(ct);var endpoint="POST:/recurring/"+id+"/prepay";var replay=await writer.ReplayAsync<List<PrepaymentAllocation>>(Actor,key,endpoint,input,ct);if(replay!=null){await tx.CommitAsync(ct);return replay;}var series=await SeriesAsync(id,ct);if(input.Weeks is <1 or >52||input.Amount<=0||decimal.Round(input.Amount,2)!=input.Amount||input.Amount>9999999999.99m||!Enum.TryParse<PaymentMethod>(input.Method,out var method)||method is not (PaymentMethod.CASH or PaymentMethod.BANK or PaymentMethod.OTHER))throw Invalid();
  var bookings=await db.Bookings.Where(x=>x.SeriesId==id&&x.Status==BookingStatus.CONFIRMED&&x.StartAt>clock.UtcNow&&x.TotalAmount>x.PaidAmount).OrderBy(x=>x.StartAt).Take(input.Weeks).ToListAsync(ct);foreach(var court in bookings.Select(x=>x.CourtId).Distinct().Order())await writer.LockAsync("court:"+court,ct);if(bookings.Count<input.Weeks||bookings.Sum(x=>x.TotalAmount-x.PaidAmount)<input.Amount)throw Invalid("PREPAYMENT_EXCEEDS_DUE");
  var block=new SeriesPrepayment{OrganizationId=series.OrganizationId,VenueId=series.VenueId,SeriesId=id,Amount=input.Amount,Weeks=input.Weeks,CreatedBy=Actor,CreatedAt=clock.UtcNow};db.SeriesPrepayments.Add(block);var remaining=input.Amount;var result=new List<PrepaymentAllocation>();foreach(var b in bookings){var amount=Math.Min(remaining,b.TotalAmount-b.PaidAmount);if(amount<=0)continue;var p=new Payment{OrganizationId=series.OrganizationId,BookingId=b.Id,PrepaymentBlockId=block.Id,Amount=amount,Method=method,Status=PaymentStatus.COMPLETED,ReceivedBy=Actor,CreatedAt=clock.UtcNow,VerifiedAt=clock.UtcNow};db.Payments.Add(p);b.PaidAmount+=amount;remaining-=amount;result.Add(new(block.Id,p.Id,b.Id,b.StartAt,amount,p.Status.ToString()));events.Add(SchedulingWriter.Event("SlotBooked",b));if(remaining==0)break;}
  writer.Audit(series.OrganizationId,Actor,"RECURRING_PREPAYMENT","SeriesPrepayment",block.Id,new{input.Weeks,input.Amount});writer.Remember(Actor,key,endpoint,input,result);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);await Publish(events,ct);return result;
 }
}
