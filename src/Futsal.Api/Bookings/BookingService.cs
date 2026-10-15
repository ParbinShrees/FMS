using System.Data;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Futsal.Api.Contracts;
using Futsal.Api.Identity;
using Futsal.Application.Abstractions;
using Futsal.Domain.Bookings;
using Futsal.Domain.Identity;
using Futsal.Domain.Locations;
using Futsal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Futsal.Api.Bookings;

public sealed class BookingService(
    FutsalDbContext db,ICurrentUser current,IClock clock,BookingPricing pricing,IBookingNotifier notifier,
    ILogger<BookingService> logger, Futsal.Api.Notifications.NotificationEvents? notifications = null)
{
    private static readonly JsonSerializerOptions JsonOptions=new(JsonSerializerDefaults.Web);

    public async Task<BookingResponse> CreateHoldAsync(CreateHoldRequest request,string idempotencyKey,IPAddress? ip,CancellationToken ct)
    {
        var userId=RequireCustomer();var endpoint="POST:/api/v1/bookings/holds";var requestHash=Hash(request);
        logger.LogDebug("Creating hold for court {CourtId} and customer {CustomerId}",request.CourtId,userId);
        await using var transaction=await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct);
        try
        {
            await AdvisoryLockAsync($"idempotency:{userId}:{idempotencyKey}",ct);
            var replay=await ReplayAsync(idempotencyKey,userId,endpoint,requestHash,ct);if(replay is not null){await transaction.CommitAsync(ct);return replay;}
            var court=await db.Courts.IgnoreQueryFilters().Include(x=>x.Venue).SingleOrDefaultAsync(x=>x.Id==request.CourtId,ct)
                ?? throw new ApiException(404,"NOT_FOUND","Court was not found.");
            var venue=court.Venue;
            await Futsal.Api.Security.SubscriptionGuard.RequireWritableAsync(db,clock,venue.OrganizationId,ct);
            if(!court.IsActive||!venue.IsActive||!venue.IsPublic||!await ActiveLocationAsync(venue.LocationId,ct))throw new ApiException(404,"NOT_FOUND","Court is not available for public booking.");
            var hasOnlinePayment = await db.VenuePaymentSettings.IgnoreQueryFilters().AnyAsync(s => s.VenueId == venue.Id && s.IsEnabled && s.MerchantCode != "", ct);
            if (!hasOnlinePayment) throw new ApiException(400, "ONLINE_PAYMENT_DISABLED", "Online booking is not available for this venue.");
            await AdvisoryLockAsync("court:"+court.Id,ct);
            var times=await ValidateSlot(request.StartAt,request.EndAt,court,venue,ct);
            var expiredEvents=await ExpireOverlappingHoldsAsync(court.Id,times.Start,times.End,ct);
            var user=await db.Users.SingleAsync(x=>x.Id==userId,ct);
            if(string.IsNullOrWhiteSpace(user.Phone))throw new ApiException(400,"VALIDATION_FAILED","A verified customer phone number is required.");
            var phone=NepaliPhone.Normalize(user.Phone);
            var customer=await db.Customers.IgnoreQueryFilters().SingleOrDefaultAsync(x=>x.OrganizationId==venue.OrganizationId&&x.UserId==userId,ct);
            if(customer is null)
            {
                customer=await db.Customers.IgnoreQueryFilters().SingleOrDefaultAsync(x=>x.OrganizationId==venue.OrganizationId&&x.Phone==phone,ct);
                if(customer is null){customer=new Customer{OrganizationId=venue.OrganizationId,UserId=userId,FullName=user.FullName,Phone=phone,CreatedAt=clock.UtcNow};db.Customers.Add(customer);}
                else customer.UserId=userId;
            }
            var activeHoldCount=await db.Bookings.IgnoreQueryFilters().Where(x=>x.Status==BookingStatus.HELD&&x.HoldExpiresAt>clock.UtcNow&&x.CustomerId!=null&&db.Customers.IgnoreQueryFilters().Any(c=>c.Id==x.CustomerId&&c.UserId==userId)).CountAsync(ct);
            if(activeHoldCount>=2)throw new ApiException(409,"TOO_MANY_HOLDS","A customer may hold at most two active bookings.");
            var amount=await pricing.CalculateAsync(court,times.Start,times.End,venue,customer,ct);
            var holdExpiry=clock.UtcNow.AddMinutes(venue.HoldMinutes);
            var booking=Booking.NewHeld();FillBooking(booking,court,customer,request.StartAt,request.EndAt,BookingSource.ONLINE,amount,venue,userId,clock.UtcNow);
            booking.HoldExpiresAt=holdExpiry;db.Bookings.Add(booking);
            var response=ToResponse(booking);
            db.IdempotencyKeys.Add(new IdempotencyKey{Key=idempotencyKey,UserId=userId,Endpoint=endpoint,RequestHash=requestHash,ResponseStatus=201,ResponseBody=JsonSerializer.Serialize(response,JsonOptions),CreatedAt=clock.UtcNow});
            AddAudit(venue.OrganizationId,userId,"BOOKING_HELD",booking.Id,null,response,ip);
            await db.SaveChangesAsync(ct);await transaction.CommitAsync(ct);
            foreach(var evt in expiredEvents)await notifier.PublishAsync(evt,ct);
            await notifier.PublishAsync(new BookingEvent("SlotHeld",venue.Id,court.Id,times.Start,times.End,holdExpiry,booking.Id),ct);
            return response;
        }
        catch(Exception ex) when(IsExclusionViolation(ex)){await transaction.RollbackAsync(ct);throw SlotUnavailable();}
        catch{await transaction.RollbackAsync(ct);throw;}
    }

    public async Task<PriceBreakdown> PreviewStaffBookingAsync(StaffBookingRequest request,CancellationToken ct)
    {
        var org=current.OrganizationId??throw new UnauthorizedAccessException();
        var court=await db.Courts.SingleOrDefaultAsync(x=>x.Id==request.CourtId&&x.OrganizationId==org,ct)??throw new ApiException(404,"NOT_FOUND","NOT_FOUND");
        var venue=await db.Venues.SingleAsync(x=>x.Id==court.VenueId,ct);var times=await ValidateSlot(request.StartAt,request.EndAt,court,venue,ct);
        var phone=NepaliPhone.Normalize(request.Phone);var customer=await db.Customers.SingleOrDefaultAsync(x=>x.Phone==phone,ct);var amount=await pricing.CalculateAsync(court,times.Start,times.End,venue,customer,ct);
        if(request.InitialPayment<0||request.InitialPayment>amount.Total)throw new ApiException(400,"INVALID_AMOUNT","INVALID_AMOUNT");return amount;
    }

    public async Task<BookingResponse> CreateStaffBookingAsync(StaffBookingRequest request,string idempotencyKey,IPAddress? ip,CancellationToken ct)
    {
        var userId=current.UserId??throw new UnauthorizedAccessException();var orgId=current.OrganizationId??throw new UnauthorizedAccessException();var endpoint="POST:/api/v1/staff/bookings";var requestHash=Hash(request);
        await using var transaction=await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct);
        try
        {
            await AdvisoryLockAsync($"idempotency:{userId}:{idempotencyKey}",ct);
            var replay=await ReplayAsync(idempotencyKey,userId,endpoint,requestHash,ct);if(replay is not null){await transaction.CommitAsync(ct);return replay;}
            var court=await db.Courts.SingleOrDefaultAsync(x=>x.Id==request.CourtId&&x.OrganizationId==orgId,ct)??throw new ApiException(404,"NOT_FOUND","Court was not found.");
            var venue=await db.Venues.SingleAsync(x=>x.Id==court.VenueId,ct);
            await Futsal.Api.Security.SubscriptionGuard.RequireWritableAsync(db,clock,venue.OrganizationId,ct);
            await AdvisoryLockAsync("court:"+court.Id,ct);
            var times=await ValidateSlot(request.StartAt,request.EndAt,court,venue,ct);
            if(times.Start<clock.UtcNow&&current.Role is not ("Owner" or "Manager"))throw new ApiException(403,"FORBIDDEN","Back-dated bookings require Manager or Owner permission.");
            var expiredEvents=await ExpireOverlappingHoldsAsync(court.Id,times.Start,times.End,ct);
            var phone=NepaliPhone.Normalize(request.Phone);
            var customer=await db.Customers.SingleOrDefaultAsync(x=>x.OrganizationId==orgId&&x.Phone==phone,ct);
            if(customer is null){customer=new Customer{OrganizationId=orgId,FullName=request.FullName.Trim(),Phone=phone,CreatedAt=clock.UtcNow};db.Customers.Add(customer);}
            else if(customer.FullName=="Customer")customer.FullName=request.FullName.Trim();
            var amount=await pricing.CalculateAsync(court,times.Start,times.End,venue,customer,ct);
            if(request.InitialPayment is > 0 && (request.InitialPaymentMethod is null||request.InitialPayment>amount.Total))throw new ApiException(400,"VALIDATION_FAILED","Initial payment requires a method and cannot exceed the booking total.");
            var booking=Booking.NewStaffConfirmed();FillBooking(booking,court,customer,request.StartAt,request.EndAt,BookingSource.STAFF,amount,venue,userId,clock.UtcNow);booking.Notes=request.Notes;
            if(request.InitialPayment is > 0){booking.PaidAmount=request.InitialPayment.Value;db.Payments.Add(new Payment{OrganizationId=orgId,BookingId=booking.Id,Method=request.InitialPaymentMethod!.Value,Status=PaymentStatus.COMPLETED,Amount=request.InitialPayment.Value,ReceivedBy=userId,CreatedAt=clock.UtcNow,VerifiedAt=clock.UtcNow});}
            db.Bookings.Add(booking);var response=ToResponse(booking);
            db.IdempotencyKeys.Add(new IdempotencyKey{Key=idempotencyKey,UserId=userId,Endpoint=endpoint,RequestHash=requestHash,ResponseStatus=201,ResponseBody=JsonSerializer.Serialize(response,JsonOptions),CreatedAt=clock.UtcNow});
            AddAudit(orgId,userId,"BOOKING_CREATED",booking.Id,null,response,ip);
            await db.SaveChangesAsync(ct);await transaction.CommitAsync(ct);
            foreach(var evt in expiredEvents)await notifier.PublishAsync(evt,ct);
            await notifier.PublishAsync(new BookingEvent("SlotBooked",venue.Id,court.Id,times.Start,times.End,null,booking.Id),ct);return response;
        }
        catch(Exception ex) when(IsExclusionViolation(ex)){await transaction.RollbackAsync(ct);throw SlotUnavailable();}
        catch{await transaction.RollbackAsync(ct);throw;}
    }

    public async Task ReleaseHoldAsync(Guid id,IPAddress? ip,CancellationToken ct)
    {
        var uid=RequireCustomer();var owned=await GetOwnedBookingAsync(id,uid,ct);var booking=owned.Booking;
        if(booking.Status!=BookingStatus.HELD||booking.HoldExpiresAt<=clock.UtcNow)throw new ApiException(409,"HOLD_EXPIRED","The booking hold has expired.");
        booking.Cancel(clock.UtcNow,"Customer released hold");AddAudit(booking.OrganizationId,uid,"BOOKING_RELEASED",id,new{status="HELD"},new{status="CANCELLED"},ip);
        await db.SaveChangesAsync(ct);await notifier.PublishAsync(new BookingEvent("SlotReleased",booking.VenueId,booking.CourtId,booking.StartAt,booking.EndAt,null,booking.Id),ct);
    }

    public async Task<List<BookingResponse>> MyBookingsAsync(CancellationToken ct)
    {
        var uid=RequireCustomer();return await db.Bookings.IgnoreQueryFilters().Where(x=>x.CustomerId!=null&&db.Customers.IgnoreQueryFilters().Any(c=>c.Id==x.CustomerId&&c.UserId==uid))
            .OrderByDescending(x=>x.StartAt).Select(x=>new BookingResponse(x.Id,x.BookingCode,x.Status,x.StartAt,x.EndAt,x.HoldExpiresAt,x.TotalAmount,x.DiscountAmount,x.AdvanceRequired,x.PaidAmount)).ToListAsync(ct);
    }

    public async Task CancelCustomerAsync(Guid id,string? reason,IPAddress? ip,CancellationToken ct)
    {
        var uid=RequireCustomer();var owned=await GetOwnedBookingAsync(id,uid,ct);var booking=owned.Booking;
        var venue=await db.Venues.IgnoreQueryFilters().SingleAsync(x=>x.Id==booking.VenueId,ct);var wasHold=booking.Status==BookingStatus.HELD;var before=new{status=booking.Status.ToString()};var forfeited=CancelWithPolicy(booking,venue,reason);
        AddAudit(booking.OrganizationId,uid,"BOOKING_CANCELLED",id,before,new{status="CANCELLED",forfeited},ip);
        await db.SaveChangesAsync(ct);await notifier.PublishAsync(new BookingEvent(wasHold?"SlotReleased":"SlotCancelled",venue.Id,booking.CourtId,booking.StartAt,booking.EndAt,null,booking.Id),ct);
    }

    public async Task RescheduleAsync(Guid id,RescheduleBookingRequest request,IPAddress? ip,CancellationToken ct)
    {
        var booking=await db.Bookings.SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new ApiException(404,"NOT_FOUND","Booking was not found.");
        var court=await db.Courts.SingleAsync(x=>x.Id==booking.CourtId,ct);var venue=await db.Venues.SingleAsync(x=>x.Id==booking.VenueId,ct);
        var before=new{booking.StartAt,booking.EndAt};
        await using var transaction=await db.Database.BeginTransactionAsync(ct);
        (DateTimeOffset Start,DateTimeOffset End) times;
        IReadOnlyList<BookingEvent> expiredEvents=[];
        try{await AdvisoryLockAsync("court:"+court.Id,ct);times=await ValidateSlot(request.StartAt,request.EndAt,court,venue,ct);expiredEvents=await ExpireOverlappingHoldsAsync(court.Id,times.Start,times.End,ct);booking.Reschedule(times.Start,times.End,clock.UtcNow);AddAudit(booking.OrganizationId,current.UserId,"BOOKING_RESCHEDULED",id,before,new{booking.StartAt,booking.EndAt},ip);await db.SaveChangesAsync(ct);await transaction.CommitAsync(ct);}
        catch(Exception ex)when(IsExclusionViolation(ex)){await transaction.RollbackAsync(ct);throw SlotUnavailable();}
        catch{await transaction.RollbackAsync(ct);throw;}
        foreach(var evt in expiredEvents)await notifier.PublishAsync(evt,ct);
        await notifier.PublishAsync(new BookingEvent("SlotReleased",venue.Id,court.Id,before.StartAt,before.EndAt,null,booking.Id),ct);
        await notifier.PublishAsync(new BookingEvent("SlotBooked",venue.Id,court.Id,times.Start,times.End,null,booking.Id),ct);
    }

    public async Task CancelStaffAsync(Guid id,string? reason,IPAddress? ip,CancellationToken ct)
    {
        var booking=await db.Bookings.SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new ApiException(404,"NOT_FOUND","Booking was not found.");var venue=await db.Venues.SingleAsync(x=>x.Id==booking.VenueId,ct);
        var wasHold=booking.Status==BookingStatus.HELD;var before=new{status=booking.Status.ToString()};var forfeited=CancelWithPolicy(booking,venue,reason);AddAudit(booking.OrganizationId,current.UserId,"BOOKING_CANCELLED",id,before,new{status="CANCELLED",forfeited},ip);
        await db.SaveChangesAsync(ct);await notifier.PublishAsync(new BookingEvent(wasHold?"SlotReleased":"SlotCancelled",venue.Id,booking.CourtId,booking.StartAt,booking.EndAt,null,booking.Id),ct);
    }

    public async Task MarkNoShowAsync(Guid id,IPAddress? ip,CancellationToken ct)
    {
        var booking=await db.Bookings.SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new ApiException(404,"NOT_FOUND","Booking was not found.");var before=new{status=booking.Status.ToString()};booking.MarkNoShow();
        if(booking.CustomerId is Guid cid){var customer=await db.Customers.SingleAsync(x=>x.Id==cid,ct);customer.NoShowCount++;}
        AddAudit(booking.OrganizationId,current.UserId,"BOOKING_NO_SHOW",id,before,new{status="NO_SHOW"},ip);await db.SaveChangesAsync(ct);
    }

    public async Task CompleteAsync(Guid id,IPAddress? ip,CancellationToken ct)
    {
        var booking=await db.Bookings.SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new ApiException(404,"NOT_FOUND","Booking was not found.");var before=new{status=booking.Status.ToString()};booking.Complete();
        AddAudit(booking.OrganizationId,current.UserId,"BOOKING_COMPLETED",id,before,new{status="COMPLETED"},ip);await db.SaveChangesAsync(ct);
    }

    public async Task RecordPaymentAsync(Guid id,RecordPaymentRequest request,string idempotencyKey,IPAddress? ip,CancellationToken ct)
    {
        var userId=current.UserId??throw new UnauthorizedAccessException();var endpoint=$"POST:/api/v1/staff/bookings/{id}/payments";var hash=Hash(request);
        await using var transaction=await db.Database.BeginTransactionAsync(ct);
        try
        {
            await AdvisoryLockAsync($"idempotency:{userId}:{idempotencyKey}",ct);
            var prior=await db.IdempotencyKeys.AsNoTracking().SingleOrDefaultAsync(x=>x.Key==idempotencyKey&&x.UserId==userId&&x.Endpoint==endpoint,ct);
            if(prior is not null){if(prior.RequestHash!=hash)throw new ApiException(409,"IDEMPOTENCY_CONFLICT","This idempotency key was already used for a different request.");await transaction.CommitAsync(ct);return;}
            var booking=await db.Bookings.SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new ApiException(404,"NOT_FOUND","Booking was not found.");
            if(request.Amount<0&&Math.Abs(request.Amount)>booking.PaidAmount)throw new ApiException(400,"VALIDATION_FAILED","Refund cannot exceed the amount already paid.");
            if(request.Amount>0&&request.Amount>booking.TotalAmount-booking.PaidAmount)throw new ApiException(400,"VALIDATION_FAILED","Payment cannot exceed the booking balance due.");
            booking.PaidAmount+=request.Amount;var paymentId=Guid.NewGuid();db.Payments.Add(new Payment{Id=paymentId,OrganizationId=booking.OrganizationId,BookingId=id,Method=request.Method,Status=request.Amount<0?PaymentStatus.REFUNDED:PaymentStatus.COMPLETED,Amount=request.Amount,GatewayRef=request.Reference,ReceivedBy=userId,CreatedAt=clock.UtcNow,VerifiedAt=clock.UtcNow});
            AddAudit(booking.OrganizationId,userId,request.Amount<0?"PAYMENT_REFUNDED":"PAYMENT_RECORDED",id,null,new{request.Amount,request.Method},ip);
            db.IdempotencyKeys.Add(new IdempotencyKey{Key=idempotencyKey,UserId=userId,Endpoint=endpoint,RequestHash=hash,ResponseStatus=204,ResponseBody=JsonSerializer.Serialize(new{paymentId,booking.PaidAmount},JsonOptions),CreatedAt=clock.UtcNow});
            await db.SaveChangesAsync(ct);await transaction.CommitAsync(ct);
            if (notifications != null) await notifications.PublishAsync(id, ct);
        }
        catch{await transaction.RollbackAsync(ct);throw;}
    }

    public async Task<List<object>> ListStaffAsync(DateTimeOffset from,DateTimeOffset to,Guid? courtId,BookingStatus? status,Guid? customerId,int page,int pageSize,CancellationToken ct)
    {
        if(to<=from)throw new ApiException(400,"VALIDATION_FAILED","to must be after from.");
        var query=db.Bookings.AsNoTracking().Where(x=>x.StartAt<to&&x.EndAt>from);
        if(courtId is not null)query=query.Where(x=>x.CourtId==courtId);if(status is not null)query=query.Where(x=>x.Status==status);if(customerId is not null)query=query.Where(x=>x.CustomerId==customerId);
        return await query.OrderBy(x=>x.StartAt).Skip((Math.Clamp(page,1,100000)-1)*Math.Clamp(pageSize,1,100)).Take(Math.Clamp(pageSize,1,100))
            .Select(x=>(object)new{ x.Id,x.BookingCode,x.CourtId,x.CustomerId,x.StartAt,x.EndAt,x.Status,x.Source,x.TotalAmount,x.PaidAmount,x.HoldExpiresAt }).ToListAsync(ct);
    }

    public async Task ExpireDueHoldsAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var holds = await db.Bookings.IgnoreQueryFilters().Where(x => x.Status == BookingStatus.HELD && x.HoldExpiresAt <= now).ToListAsync(ct);
        if (holds.Count == 0) return;

        var recentPendingCutoff = now.AddMinutes(-5);
        var activePendingBookingIds = await db.Payments.IgnoreQueryFilters()
            .Where(p => p.Status == PaymentStatus.PENDING && p.BookingId != null && p.CreatedAt >= recentPendingCutoff)
            .Select(p => p.BookingId!.Value)
            .Distinct()
            .ToListAsync(ct);

        var holdsToExpire = holds.Where(h => !activePendingBookingIds.Contains(h.Id)).ToList();
        if (holdsToExpire.Count == 0) return;

        foreach (var booking in holdsToExpire) booking.Expire();
        await db.SaveChangesAsync(ct);
        foreach (var booking in holdsToExpire)
            await notifier.PublishAsync(new BookingEvent("SlotReleased", booking.VenueId, booking.CourtId, booking.StartAt, booking.EndAt, null, booking.Id), ct);
    }

    private async Task<(DateTimeOffset Start,DateTimeOffset End)> ValidateSlot(DateTimeOffset startInput,DateTimeOffset endInput,Court court,Venue venue,CancellationToken ct)
    {
        var start=startInput.ToUniversalTime();var end=endInput.ToUniversalTime();var zone=TimeZoneInfo.FindSystemTimeZoneById(venue.Timezone);
        if(!court.IsActive||!venue.IsActive||end<=start)throw new ApiException(400,"VALIDATION_FAILED","The requested court or time range is invalid.");
        var localStart=TimeZoneInfo.ConvertTime(start,zone);var localEnd=TimeZoneInfo.ConvertTime(end,zone);var localNow=TimeZoneInfo.ConvertTime(clock.UtcNow,zone);
        if(localStart<=localNow)throw new ApiException(400,"VALIDATION_FAILED","Bookings must be in the future in the venue timezone.");
        if(localStart.Date!=localEnd.Date)throw new ApiException(400,"VALIDATION_FAILED","A booking must fit within one local operating day.");
        if(court.SlotMinutes is not (30 or 60 or 90))throw new ApiException(400,"VALIDATION_FAILED","Court slot length must be 30, 60 or 90 minutes.");
        var minutes=(end-start).TotalMinutes;if(minutes<=0||minutes%court.SlotMinutes!=0)throw new ApiException(400,"VALIDATION_FAILED","Booking duration must be aligned to court slots.");
        var weekday=(short)localStart.DayOfWeek;var hours=await db.CourtHours.SingleOrDefaultAsync(x=>x.CourtId==court.Id&&x.Weekday==weekday,ct);
        if(hours is null)throw new ApiException(400,"VALIDATION_FAILED","The court is closed on the requested day.");
        var from=TimeOnly.FromDateTime(localStart.DateTime);var to=TimeOnly.FromDateTime(localEnd.DateTime);
        var offset=(from.ToTimeSpan()-hours.OpenTime.ToTimeSpan()).TotalMinutes;
        if(from<hours.OpenTime||to>hours.CloseTime||offset<0||offset%court.SlotMinutes!=0)throw new ApiException(400,"VALIDATION_FAILED","The booking is outside operating hours or not aligned to the slot grid.");
        if(await db.BlockedPeriods.IgnoreQueryFilters().AnyAsync(x=>x.CourtId==court.Id&&x.StartAt<end&&x.EndAt>start,ct))throw new ApiException(409,"SLOT_UNAVAILABLE","The court is blocked during this time.");
        return(start,end);
    }

    private async Task<IReadOnlyList<BookingEvent>> ExpireOverlappingHoldsAsync(Guid courtId,DateTimeOffset start,DateTimeOffset end,CancellationToken ct)
    {
        var query=db.Bookings.IgnoreQueryFilters().Where(x=>x.CourtId==courtId&&x.Status==BookingStatus.HELD&&x.HoldExpiresAt<clock.UtcNow&&x.StartAt<end&&x.EndAt>start);
        var events=await query.Select(x=>new BookingEvent("SlotReleased",x.VenueId,x.CourtId,x.StartAt,x.EndAt,null,x.Id)).ToListAsync(ct);
        await query.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,BookingStatus.EXPIRED).SetProperty(x=>x.HoldExpiresAt,(DateTimeOffset?)null).SetProperty(x=>x.UpdatedAt,clock.UtcNow),ct);
        return events;
    }

    private async Task AdvisoryLockAsync(string value,CancellationToken ct)
    {
        if(db.Database.IsRelational())await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({value},0))",ct);
    }
    private async Task<BookingResponse?> ReplayAsync(string key,Guid userId,string endpoint,string hash,CancellationToken ct)
    {
        var previous=await db.IdempotencyKeys.AsNoTracking().SingleOrDefaultAsync(x=>x.Key==key&&x.UserId==userId&&x.Endpoint==endpoint,ct);if(previous is null)return null;
        if(previous.RequestHash!=hash)throw new ApiException(409,"IDEMPOTENCY_CONFLICT","This idempotency key was already used for a different request.");
        return JsonSerializer.Deserialize<BookingResponse>(previous.ResponseBody!,JsonOptions)??throw new InvalidOperationException("Stored idempotency response is invalid.");
    }
    private static string Hash<T>(T request)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request,JsonOptions)));
    private static bool IsExclusionViolation(Exception ex)=>ex is DbUpdateException{InnerException:PostgresException{SqlState:"23P01"}} or PostgresException{SqlState:"23P01"};
    private static ApiException SlotUnavailable()=>new(409,"SLOT_UNAVAILABLE","That court slot has just been taken.");
    private Guid RequireCustomer()=>current.IsAuthenticated&&current.Role=="Customer"&&current.UserId is Guid id?id:throw new ApiException(403,"FORBIDDEN","Customer authentication is required.");
    private async Task<(Booking Booking,Customer Customer)> GetOwnedBookingAsync(Guid bookingId,Guid userId,CancellationToken ct)
    {
        var ids=await db.Customers.IgnoreQueryFilters().Where(c=>c.UserId==userId).Select(c=>c.Id).ToArrayAsync(ct);
        var booking=await db.Bookings.IgnoreQueryFilters().SingleOrDefaultAsync(b=>b.Id==bookingId&&b.CustomerId!=null&&ids.Contains(b.CustomerId.Value),ct)
            ??throw new ApiException(404,"NOT_FOUND","Booking was not found.");
        var customer=await db.Customers.IgnoreQueryFilters().SingleAsync(c=>c.Id==booking.CustomerId,ct);
        return(booking,customer);
    }
    private async Task<bool> ActiveLocationAsync(Guid locationId,CancellationToken ct)=>await db.Locations.AnyAsync(x=>x.Id==locationId&&x.IsActive,ct);
    private static void FillBooking(Booking b,Court court,Customer customer,DateTimeOffset start,DateTimeOffset end,BookingSource source,PriceBreakdown price,Venue venue,Guid? creator,DateTimeOffset now)
    {
        b.OrganizationId=court.OrganizationId;b.VenueId=court.VenueId;b.CourtId=court.Id;b.CustomerId=customer.Id;b.BookingCode=NewCode();b.StartAt=start.ToUniversalTime();b.EndAt=end.ToUniversalTime();b.Source=source;
        b.TotalAmount=price.Total;b.DiscountAmount=price.Discount;b.AdvanceRequired=venue.AdvanceType=="FIXED"?Math.Min(price.Total,venue.AdvanceValue):decimal.Round(price.Total*venue.AdvanceValue/100m,2,MidpointRounding.AwayFromZero);b.CreatedBy=creator;
        b.CreatedAt=now;b.UpdatedAt=now;
    }
    private static string NewCode()
    {
        const string alphabet="23456789ABCDEFGHJKLMNPQRSTUVWXYZ";Span<byte> bytes=stackalloc byte[5];RandomNumberGenerator.Fill(bytes);return "FB-"+string.Concat(bytes.ToArray().Select(x=>alphabet[x%alphabet.Length]));
    }
    private void AddAudit(Guid org,Guid? user,string action,Guid entityId,object? before,object? after,IPAddress? ip)=>db.AuditLogs.Add(new AuditLog{OrganizationId=org,UserId=user,Action=action,EntityType="Booking",EntityId=entityId,Before=before is null?null:JsonSerializer.Serialize(before,JsonOptions),After=after is null?null:JsonSerializer.Serialize(after,JsonOptions),Ip=ip,CreatedAt=clock.UtcNow});
    private static BookingResponse ToResponse(Booking b)=>new(b.Id,b.BookingCode,b.Status,b.StartAt,b.EndAt,b.HoldExpiresAt,b.TotalAmount,b.DiscountAmount,b.AdvanceRequired,b.PaidAmount);
    private bool CancelWithPolicy(Booking booking,Venue venue,string? reason)
    {
        if(booking.Status==BookingStatus.HELD&&booking.HoldExpiresAt<=clock.UtcNow)throw new ApiException(409,"HOLD_EXPIRED","The booking hold has expired.");
        var forfeited=booking.StartAt-clock.UtcNow<TimeSpan.FromHours(venue.FreeCancelHours);
        booking.Cancel(clock.UtcNow,forfeited?string.Join("; ",new[]{reason,"Late cancellation; advance forfeited"}.Where(x=>!string.IsNullOrWhiteSpace(x))):reason);
        return forfeited;
    }
}
