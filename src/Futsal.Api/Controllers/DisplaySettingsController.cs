using Futsal.Infrastructure.Persistence;
using Futsal.Api.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace Futsal.Api.Controllers;
[ApiController,Route("api/v1/admin/venues/{id:guid}/display-settings"),Authorize(Policy="CanManageStaff")]
public sealed class DisplaySettingsController(FutsalDbContext db):ControllerBase
{
 public sealed record DisplaySettings(bool BsFirst,bool NepaliNumerals);
 [HttpGet]public async Task<IActionResult> Get(Guid id,CancellationToken ct){var v=await db.Venues.SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new ApiException(404,"NOT_FOUND","Venue not found.");return Ok(new DisplaySettings(v.BsFirst,v.NepaliNumerals));}
 [HttpPut]public async Task<IActionResult> Put(Guid id,DisplaySettings input,CancellationToken ct){var v=await db.Venues.SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new ApiException(404,"NOT_FOUND","Venue not found.");v.BsFirst=input.BsFirst;v.NepaliNumerals=input.NepaliNumerals;await db.SaveChangesAsync(ct);return NoContent();}
}
