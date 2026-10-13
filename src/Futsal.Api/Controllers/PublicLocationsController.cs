using Futsal.Domain.Locations;
using Futsal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Futsal.Api.Controllers;

public sealed record PublicVenueResult(Guid Id,string Name,string LocationPath,string Slug,double? DistanceKm,decimal? LowestPrice,string? PhotoPlaceholder,int CourtCount,string[] Surfaces,string[] AmenityCodes);

[ApiController, Route("api/v1/public")]
public sealed class PublicLocationsController(FutsalDbContext db) : ControllerBase
{
    [HttpGet("locations")]
    public async Task<IActionResult> Locations([FromQuery] Guid? parentId, [FromQuery] LocationType? type, CancellationToken ct)
    {
        var query = db.Locations.AsNoTracking().Where(x => x.IsActive);
        query = parentId is null ? query.Where(x => x.ParentId == null) : query.Where(x => x.ParentId == parentId);
        if (type is not null) query = query.Where(x => x.Type == type);
        return Ok(await query.OrderBy(x => x.SortOrder).ThenBy(x => x.NameEn).Select(x => new { x.Id, x.ParentId, x.Type, x.NameEn, x.NameNe, x.Slug, x.Path, x.Latitude, x.Longitude, x.IsSeeded }).ToListAsync(ct));
    }

    [HttpGet("locations/search")]
    public async Task<IActionResult> Search([FromQuery] string q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q)) return Ok(Array.Empty<object>());
        var needle = q.Trim();
        return Ok((await db.Locations.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.SortOrder).ToListAsync(ct))
            .Where(x => x.NameEn.Contains(needle, StringComparison.OrdinalIgnoreCase) || x.NameNe.Contains(needle, StringComparison.Ordinal))
            .Select(x => new { x.Id,x.ParentId,x.Type,x.NameEn,x.NameNe,x.Slug,x.Path,x.IsSeeded }));
    }

    [HttpGet("locations/{slug}/venues")]
    public async Task<IActionResult> Venues(string slug, CancellationToken ct)
    {
        var location = await db.Locations.AsNoTracking().FirstOrDefaultAsync(x => x.Slug == slug && x.IsActive, ct);
        if (location is null) return NotFound();
        return Ok(await VenueResults(db.Venues.IgnoreQueryFilters().AsNoTracking().Where(v => v.IsPublic && db.Locations.Any(l => l.Id == v.LocationId && l.IsActive && (l.Id == location.Id || l.Path.StartsWith(location.Path + "/")))), null, ct));
    }

    [HttpGet("venues")]
    public async Task<IActionResult> Venues([FromQuery] string? city, [FromQuery] string? area, [FromQuery] string? near, [FromQuery] double? radiusKm,
        [FromQuery] string? surface, [FromQuery] decimal? minPrice, [FromQuery] decimal? maxPrice, [FromQuery] string? amenities, [FromQuery] string? sort, CancellationToken ct)
    {
        (double Lat, double Lng)? point = null;
        if (!string.IsNullOrWhiteSpace(near))
        {
            var parts = near.Split(',');
            if (parts.Length != 2 || !double.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out var lat) || !double.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out var lng) || lat is < 26 or > 31 || lng is < 80 or > 89)
                return BadRequest(new { code="VALIDATION_FAILED", title="near must be latitude,longitude within Nepal." });
            point = (lat,lng);
        }
        var query = db.Venues.IgnoreQueryFilters().AsNoTracking().Where(v => v.IsPublic && db.Locations.Any(l => l.Id == v.LocationId && l.IsActive));
        if (!string.IsNullOrWhiteSpace(city))
        {
            var cityId = Guid.TryParse(city, out var parsedCity) ? parsedCity : await db.Locations.Where(l=>l.IsActive && l.Type==LocationType.CITY && l.Slug==city).Select(l=>(Guid?)l.Id).SingleOrDefaultAsync(ct);
            if (cityId is null) return Ok(Array.Empty<PublicVenueResult>());
            query = query.Where(v => v.CityId == cityId);
        }
        if (!string.IsNullOrWhiteSpace(area))
        {
            var areaId = Guid.TryParse(area, out var parsedArea) ? parsedArea : await db.Locations.Where(l=>l.IsActive && l.Type==LocationType.AREA && l.Slug==area).Select(l=>(Guid?)l.Id).SingleOrDefaultAsync(ct);
            if (areaId is null) return Ok(Array.Empty<PublicVenueResult>());
            query = query.Where(v => v.LocationId == areaId);
        }
        if (surface is not null) query = query.Where(v => v.Courts.Any(c => c.IsActive && c.Surface == surface));
        if (minPrice is not null || maxPrice is not null) query = query.Where(v => v.Courts.Any(c => c.IsActive
            && (minPrice == null || c.PricePerSlot >= minPrice) && (maxPrice == null || c.PricePerSlot <= maxPrice)));
        if (!string.IsNullOrWhiteSpace(amenities))
        {
            var codes = amenities.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            foreach (var code in codes) query = query.Where(v => v.VenueAmenities.Any(va => va.Amenity.Code == code && va.Amenity.IsActive));
        }
        var items = await VenueResults(query, point, ct);
        if (point is { } p) items = items.Where(x => x.DistanceKm is not null && (radiusKm is null || x.DistanceKm <= radiusKm)).ToList();
        items = (sort?.ToLowerInvariant()) switch { "distance" when point is not null => items.OrderBy(x=>x.DistanceKm).ToList(), "price" => items.OrderBy(x=>x.LowestPrice).ToList(), "name" => items.OrderBy(x=>x.Name).ToList(), _ => items };
        return Ok(items);
    }

    private static async Task<List<PublicVenueResult>> VenueResults(IQueryable<Venue> query, (double Lat,double Lng)? point, CancellationToken ct)
    {
        var rows = await query.Select(v => new { v.Id,v.Name,v.Slug,v.Latitude,v.Longitude,v.PhotoPlaceholder, Path=v.Location.Path,
            Price=v.Courts.Where(c=>c.IsActive).Select(c=>(decimal?)c.PricePerSlot).Min(), Count=v.Courts.Count(c=>c.IsActive),
            Surfaces=v.Courts.Where(c=>c.IsActive && c.Surface != null).Select(c=>c.Surface!).Distinct().ToArray(),
            Amenities=v.VenueAmenities.Where(a=>a.Amenity.IsActive).Select(a=>a.Amenity.Code).ToArray() }).ToListAsync(ct);
        return rows.Select(v => new PublicVenueResult(v.Id,v.Name,v.Path,v.Slug,point is { } p && v.Latitude is not null && v.Longitude is not null ? Haversine(p.Lat,p.Lng,(double)v.Latitude.Value,(double)v.Longitude.Value) : null,v.Price,v.PhotoPlaceholder ?? "placeholder",v.Count,v.Surfaces,v.Amenities)).ToList();
    }
    private static double Haversine(double lat1,double lon1,double lat2,double lon2) { const double r=6371; var dLat=(lat2-lat1)*Math.PI/180; var dLon=(lon2-lon1)*Math.PI/180; var a=Math.Sin(dLat/2)*Math.Sin(dLat/2)+Math.Cos(lat1*Math.PI/180)*Math.Cos(lat2*Math.PI/180)*Math.Sin(dLon/2)*Math.Sin(dLon/2); return r*2*Math.Atan2(Math.Sqrt(a),Math.Sqrt(1-a)); }
}
