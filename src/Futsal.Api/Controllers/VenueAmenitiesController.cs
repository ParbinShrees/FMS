using Futsal.Application.Abstractions;
using Futsal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Futsal.Api.Controllers;

[ApiController, Route("api/v1/admin/venues/{venueId:guid}/amenities"), Authorize(Policy="StaffAccess")]
public sealed class VenueAmenitiesController(FutsalDbContext db, ICurrentUser current) : ControllerBase
{
    public sealed record AmenitySelection(string[] Codes);

    [HttpPut]
    public async Task<IActionResult> Set(Guid venueId,AmenitySelection selection,CancellationToken ct)
    {
        if(current.IsSuperAdmin || current.Role!="Owner") return Forbid();
        var venue=await db.Venues.SingleOrDefaultAsync(v=>v.Id==venueId,ct);
        if(venue is null)return NotFound();
        var amenities=await db.Amenities.Where(a=>a.IsActive && selection.Codes.Contains(a.Code)).ToListAsync(ct);
        if(amenities.Count!=selection.Codes.Distinct(StringComparer.OrdinalIgnoreCase).Count())return BadRequest(new{code="UNKNOWN_AMENITY"});
        db.VenueAmenities.RemoveRange(await db.VenueAmenities.Where(x=>x.VenueId==venueId).ToListAsync(ct));
        foreach(var item in amenities)db.VenueAmenities.Add(new(){VenueId=venueId,AmenityId=item.Id});
        await db.SaveChangesAsync(ct);return NoContent();
    }
}
