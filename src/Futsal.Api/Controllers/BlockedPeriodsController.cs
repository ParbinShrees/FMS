using Futsal.Api.Bookings;
using Futsal.Api.Identity;
using Futsal.Application.Abstractions;
using Futsal.Domain.Bookings;
using Futsal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Futsal.Api.Controllers;

public sealed record CreateBlockedPeriodRequest(DateTimeOffset StartAt,DateTimeOffset EndAt,string? Reason);

[ApiController,Route("api/v1/staff/courts/{courtId:guid}/blocked-periods"),Authorize(Policy="CanManageStaff")]
public sealed class BlockedPeriodsController(FutsalDbContext db,ICurrentUser current,IBookingNotifier notifier):ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(Guid courtId,CreateBlockedPeriodRequest request,CancellationToken ct)
    {
        if(request.EndAt<=request.StartAt)throw new ApiException(400,"VALIDATION_FAILED","endAt must be after startAt.");
        var organizationId=current.OrganizationId??throw new ApiException(403,"FORBIDDEN","An organization staff login is required.");
        var court=await db.Courts.SingleOrDefaultAsync(c=>c.Id==courtId&&c.OrganizationId==organizationId,ct)??throw new ApiException(404,"NOT_FOUND","Court was not found.");
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"court:"+court.Id},0))",ct);
        if(await db.Bookings.IgnoreQueryFilters().AnyAsync(b=>b.CourtId==court.Id&&b.Status!=BookingStatus.CANCELLED&&b.Status!=BookingStatus.EXPIRED&&b.StartAt<request.EndAt&&b.EndAt>request.StartAt,ct))
            throw new ApiException(409,"SLOT_UNAVAILABLE","A booking already occupies part of this period.");
        var block=new BlockedPeriod{OrganizationId=organizationId,CourtId=court.Id,StartAt=request.StartAt.ToUniversalTime(),EndAt=request.EndAt.ToUniversalTime(),Reason=request.Reason};
        db.BlockedPeriods.Add(block);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
        await notifier.PublishAsync(new BookingEvent("SlotBlocked",court.VenueId,court.Id,block.StartAt,block.EndAt),ct);
        return Created($"/api/v1/staff/courts/{court.Id}/blocked-periods/{block.Id}",new{id=block.Id,block.StartAt,block.EndAt,block.Reason});
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Remove(Guid courtId,Guid id,CancellationToken ct)
    {
        var block=await db.BlockedPeriods.SingleOrDefaultAsync(b=>b.Id==id&&b.CourtId==courtId,ct)??throw new ApiException(404,"NOT_FOUND","Blocked period was not found.");
        var court=await db.Courts.SingleAsync(c=>c.Id==courtId,ct);var start=block.StartAt;var end=block.EndAt;
        db.BlockedPeriods.Remove(block);await db.SaveChangesAsync(ct);
        await notifier.PublishAsync(new BookingEvent("SlotReleased",court.VenueId,court.Id,start,end),ct);
        return NoContent();
    }
}
