using System.Security.Cryptography;
using System.Text.Json;
using Futsal.Api.Identity;
using Futsal.Api.Bookings;
using Futsal.Application.Abstractions;
using Futsal.Application.Scheduling;
using Futsal.Domain.Bookings;
using Futsal.Domain.Locations;
using Futsal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace Futsal.Api.Scheduling;
public sealed class SchedulingWriter(FutsalDbContext db,IClock clock,BookingPricing pricing)
{
 public Task LockAsync(string name,CancellationToken ct)=>db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({name},0))",ct);
 public void Audit(Guid org,Guid? actor,string action,string type,Guid id,object? data=null)=>db.AuditLogs.Add(new(){OrganizationId=org,UserId=actor,Action=action,EntityType=type,EntityId=id,After=data==null?null:JsonSerializer.Serialize(data),CreatedAt=clock.UtcNow});
 public static bool Overlap(Exception ex)=>ex is DbUpdateException{InnerException:Npgsql.PostgresException{SqlState:"23P01"}} or Npgsql.PostgresException{SqlState:"23P01"};
 public async Task<string> CheckAsync(Court court,Venue venue,DateTimeOffset start,DateTimeOffset end,CancellationToken ct,Guid? except=null)
 {
  if(start<=clock.UtcNow)return "PAST";
  if(!court.IsActive||!venue.IsActive||end<=start||court.SlotMinutes is not (30 or 60 or 90))return "CLOSED";
  var local=TimeZoneInfo.ConvertTimeBySystemTimeZoneId(start,venue.Timezone);var until=TimeZoneInfo.ConvertTimeBySystemTimeZoneId(end,venue.Timezone);
  var hours=await db.CourtHours.IgnoreQueryFilters().SingleOrDefaultAsync(x=>x.CourtId==court.Id&&x.Weekday==(short)local.DayOfWeek,ct);
  if(hours==null||local.Date!=until.Date||TimeOnly.FromDateTime(local.DateTime)<hours.OpenTime||TimeOnly.FromDateTime(until.DateTime)>hours.CloseTime||(local.TimeOfDay-hours.OpenTime.ToTimeSpan()).TotalMinutes%court.SlotMinutes!=0||(end-start).TotalMinutes%court.SlotMinutes!=0)return "CLOSED";
  if(await db.BlockedPeriods.IgnoreQueryFilters().AnyAsync(x=>x.CourtId==court.Id&&x.StartAt<end&&x.EndAt>start,ct))return "BLOCKED";
  if(await db.Bookings.IgnoreQueryFilters().AnyAsync(x=>x.CourtId==court.Id&&x.Id!=except&&x.StartAt<end&&x.EndAt>start&&x.Status!=BookingStatus.CANCELLED&&x.Status!=BookingStatus.EXPIRED&&!(x.Status==BookingStatus.HELD&&x.HoldExpiresAt<=clock.UtcNow),ct))return "BOOKED";
  return "FREE";
 }
 public async Task ExpireStaleAsync(Guid courtId,DateTimeOffset start,DateTimeOffset end,List<BookingEvent> events,CancellationToken ct)
 {
  var expired=await db.Bookings.IgnoreQueryFilters().Where(x=>x.CourtId==courtId&&x.Status==BookingStatus.HELD&&x.HoldExpiresAt<=clock.UtcNow&&x.StartAt<end&&x.EndAt>start).ToListAsync(ct);
  foreach(var b in expired){b.Expire();b.UpdatedAt=clock.UtcNow;Audit(b.OrganizationId,null,"BOOKING_EXPIRED","Booking",b.Id);events.Add(Event("SlotReleased",b));}if(expired.Count>0)await db.SaveChangesAsync(ct);
 }
 public Task<PriceBreakdown> PriceAsync(Court court,Venue venue,Customer customer,DateTimeOffset start,DateTimeOffset end,bool discount,CancellationToken ct)=>pricing.CalculateAsync(court,start,end,venue,discount?customer:null,ct);
 public Booking New(Court court,Venue venue,Customer customer,DateTimeOffset start,DateTimeOffset end,decimal price,Guid? actor,bool hold=false)
 {
  if(price<0||price>9999999999.99m||decimal.Round(price,2)!=price)throw new ApiException(400,"INVALID_AMOUNT","INVALID_AMOUNT");
  var b=hold?Booking.NewHeld():Booking.NewStaffConfirmed();b.OrganizationId=venue.OrganizationId;b.VenueId=venue.Id;b.CourtId=court.Id;b.CustomerId=customer.Id;b.StartAt=start.ToUniversalTime();b.EndAt=end.ToUniversalTime();b.TotalAmount=price;b.AdvanceRequired=venue.AdvanceType=="FIXED"?Math.Min(price,venue.AdvanceValue):decimal.Round(price*venue.AdvanceValue/100m,2,MidpointRounding.AwayFromZero);b.Source=hold?BookingSource.ONLINE:BookingSource.RECURRING;b.CreatedBy=actor;b.CreatedAt=clock.UtcNow;b.UpdatedAt=clock.UtcNow;b.BookingCode="FB-"+Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();return b;
 }
 public bool Cancel(Booking b,Venue venue,string? reason,Guid? actor,List<BookingEvent> events)
 {
  if(b.StartAt<=clock.UtcNow)throw new ApiException(409,"PAST_OCCURRENCE","PAST_OCCURRENCE");var forfeited=b.StartAt-clock.UtcNow<TimeSpan.FromHours(venue.FreeCancelHours);b.Cancel(clock.UtcNow,forfeited?string.Join("; ",new[]{reason,"Late cancellation; advance forfeited"}):reason);Audit(b.OrganizationId,actor,"BOOKING_CANCELLED","Booking",b.Id,new{forfeited});events.Add(Event("SlotCancelled",b));return forfeited;
 }
 public static BookingEvent Event(string name,Booking b)=>new(name,b.VenueId,b.CourtId,b.StartAt,b.EndAt,b.HoldExpiresAt,b.Id);
 public static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
 public async Task<T?> ReplayAsync<T>(Guid user,string key,string endpoint,object request,CancellationToken ct)where T:class
 {
  if(string.IsNullOrWhiteSpace(key)||key.Length>200)throw new ApiException(400,"VALIDATION_FAILED","VALIDATION_FAILED");await LockAsync("idempotency:"+user+":"+key,ct);var prior=await db.IdempotencyKeys.SingleOrDefaultAsync(x=>x.Key==key&&x.UserId==user&&x.Endpoint==endpoint,ct);if(prior==null)return null;if(prior.RequestHash!=Hash(request))throw new ApiException(409,"IDEMPOTENCY_CONFLICT","IDEMPOTENCY_CONFLICT");return JsonSerializer.Deserialize<T>(prior.ResponseBody!,Json)!;
 }
 public void Remember(Guid user,string key,string endpoint,object request,object response)=>db.IdempotencyKeys.Add(new(){Key=key,UserId=user,Endpoint=endpoint,RequestHash=Hash(request),ResponseStatus=200,ResponseBody=JsonSerializer.Serialize(response,Json),CreatedAt=clock.UtcNow});
 static string Hash(object value)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value,Json)));
}
