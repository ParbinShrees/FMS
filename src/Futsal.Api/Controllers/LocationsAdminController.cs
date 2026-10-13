using System.Text;
using Futsal.Application.Abstractions;
using Futsal.Domain.Locations;
using Futsal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Futsal.Api.Controllers;

public sealed record LocationWrite(string NameEn,string NameNe,LocationType Type,Guid? ParentId,decimal? Latitude,decimal? Longitude,int SortOrder=0,string? Slug=null);

[ApiController, Route("api/v1/admin/locations"), Authorize]
public sealed class LocationsAdminController(FutsalDbContext db, ICurrentUser current) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(LocationWrite input,CancellationToken ct)
    {
        if (!current.IsSuperAdmin && (current.Role != "Owner" || input.Type != LocationType.AREA
            || current.UserId is not Guid userId || current.OrganizationId is not Guid organizationId
            || !await db.Memberships.AnyAsync(m => m.UserId == userId && m.OrganizationId == organizationId && m.Role == Futsal.Domain.Identity.OrganizationRole.OWNER, ct))) return Forbid();
        var parent = input.ParentId is null ? null : await db.Locations.IgnoreQueryFilters().SingleOrDefaultAsync(x=>x.Id==input.ParentId && x.IsActive,ct);
        if (input.ParentId is not null && parent is null) return BadRequest(new { code="INVALID_PARENT" });
        var expectedParent = input.Type switch { LocationType.PROVINCE => (LocationType?)null, LocationType.DISTRICT => LocationType.PROVINCE, LocationType.CITY => LocationType.DISTRICT, LocationType.AREA => LocationType.CITY, _ => null };
        if ((input.Type == LocationType.PROVINCE && parent is not null) || (input.Type != LocationType.PROVINCE && parent?.Type != expectedParent)) return BadRequest(new { code="INVALID_PARENT" });
        if (input.Type != LocationType.AREA && !current.IsSuperAdmin) return Forbid();
        if (!current.IsSuperAdmin && (current.OrganizationId is null || !await db.Venues.AnyAsync(v=>v.OrganizationId==current.OrganizationId && v.CityId==parent!.Id,ct))) return Forbid();
        if ((input.Latitude is null) != (input.Longitude is null) || input.Latitude is < 26 or > 31 || input.Longitude is < 80 or > 89) return BadRequest(new { code="INVALID_COORDINATES" });
        var slug = Slugify(input.Slug ?? input.NameEn);
        if(string.IsNullOrWhiteSpace(input.NameEn)||string.IsNullOrWhiteSpace(input.NameNe)||string.IsNullOrWhiteSpace(slug))return BadRequest(new{code="VALIDATION_FAILED"});
        if (await db.Locations.IgnoreQueryFilters().AnyAsync(x=>x.Slug==slug,ct)) return Conflict(new { code="DUPLICATE_SLUG" });
        var location = new Location { NameEn=input.NameEn.Trim(),NameNe=input.NameNe.Trim(),Type=input.Type,ParentId=input.ParentId,Slug=slug,Path=parent is null?slug:parent.Path+"/"+slug,Latitude=input.Latitude,Longitude=input.Longitude,SortOrder=input.SortOrder,IsActive=true,IsSeeded=false };
        db.Locations.Add(location); await db.SaveChangesAsync(ct);
        return Created($"/api/v1/public/locations/{location.Slug}",new { location.Id,location.Slug,location.Path,location.IsSeeded });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy="SuperAdmin")]
    public async Task<IActionResult> Update(Guid id,LocationWrite input,CancellationToken ct)
    {
        var item=await db.Locations.IgnoreQueryFilters().SingleOrDefaultAsync(x=>x.Id==id,ct); if(item is null)return NotFound();
        var parent=input.ParentId is null?null:await db.Locations.IgnoreQueryFilters().SingleOrDefaultAsync(x=>x.Id==input.ParentId && x.IsActive,ct);
        var expectedParent=input.Type switch{LocationType.PROVINCE=>(LocationType?)null,LocationType.DISTRICT=>LocationType.PROVINCE,LocationType.CITY=>LocationType.DISTRICT,LocationType.AREA=>LocationType.CITY,_=>null};
        if((input.Type==LocationType.PROVINCE&&parent is not null)||(input.Type!=LocationType.PROVINCE&&parent?.Type!=expectedParent)||parent?.Path.StartsWith(item.Path+"/")==true)return BadRequest(new{code="INVALID_PARENT"});
        var oldPath=item.Path; var slug=Slugify(input.Slug??input.NameEn);
        if(await db.Locations.IgnoreQueryFilters().AnyAsync(x=>x.Id!=id&&x.Slug==slug,ct))return Conflict(new{code="DUPLICATE_SLUG"});
        item.NameEn=input.NameEn.Trim();item.NameNe=input.NameNe.Trim();item.Slug=slug;item.Type=input.Type;item.ParentId=input.ParentId;item.Latitude=input.Latitude;item.Longitude=input.Longitude;item.SortOrder=input.SortOrder;
        item.Path=parent is null?slug:parent.Path+"/"+slug;
        foreach(var child in await db.Locations.IgnoreQueryFilters().Where(x=>x.Path.StartsWith(oldPath+"/")).ToListAsync(ct)) child.Path=item.Path+child.Path[oldPath.Length..];
        await db.SaveChangesAsync(ct);return NoContent();
    }

    [HttpDelete("{id:guid}"),Authorize(Policy="SuperAdmin")]
    public async Task<IActionResult> Deactivate(Guid id,CancellationToken ct){var item=await db.Locations.IgnoreQueryFilters().SingleOrDefaultAsync(x=>x.Id==id,ct);if(item is null)return NotFound();item.IsActive=false;await db.SaveChangesAsync(ct);return NoContent();}

    private static string Slugify(string value){var normalized=value.Normalize(NormalizationForm.FormD);var b=new StringBuilder();foreach(var c in normalized){if(System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)==System.Globalization.UnicodeCategory.NonSpacingMark)continue;if(char.IsAsciiLetterOrDigit(c))b.Append(char.ToLowerInvariant(c));else if(b.Length>0&&b[^1]!='-')b.Append('-');}return b.ToString().Trim('-');}
}
