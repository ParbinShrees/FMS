using Futsal.Domain.Locations;
using Futsal.Api.Contracts;
using Futsal.Domain.Bookings;
using Futsal.Api.Bookings;
using Futsal.Application.Abstractions;
using Futsal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text.Json;

namespace Futsal.Api.Controllers;

public sealed record PublicVenueResult(Guid Id,string Name,string LocationPath,string Slug,double? DistanceKm,decimal? LowestPrice,string? PhotoPlaceholder,int CourtCount,string[] Surfaces,string[] AmenityCodes);

[ApiController, Route("api/v1/public")]
public sealed class PublicLocationsController(FutsalDbContext db,IClock clock,BookingPricing pricing) : ControllerBase
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
        var query = db.Venues.IgnoreQueryFilters().AsNoTracking().Where(v => v.IsPublic && v.IsActive && db.Locations.Any(l => l.Id == v.LocationId && l.IsActive));
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

    [HttpGet("venues/{slug}")]
    public async Task<IActionResult> VenueDetails(string slug, CancellationToken ct)
    {
        var venue = await db.Venues.IgnoreQueryFilters().AsNoTracking()
            .Include(v => v.Courts.Where(c => c.IsActive))
            .Include(v => v.VenueAmenities).ThenInclude(va => va.Amenity)
            .SingleOrDefaultAsync(v => v.Slug == slug && v.IsPublic && v.IsActive && db.Locations.Any(l => l.Id == v.LocationId && l.IsActive), ct);

        if (venue == null) return NotFound();

        var hasOnlinePayment = await db.VenuePaymentSettings.IgnoreQueryFilters()
            .AnyAsync(s => s.VenueId == venue.Id && s.Method == PaymentMethod.ESEWA && s.IsEnabled && s.MerchantCode != "", ct);

        var courts = venue.Courts.Select(c => new Futsal.Contracts.PublicCourtDto(c.Id, c.Name, c.Surface, c.SlotMinutes, c.PricePerSlot)).ToList();
        var amenities = venue.VenueAmenities.Where(va => va.Amenity.IsActive).Select(va => va.Amenity.Code).ToList();

        return Ok(new Futsal.Contracts.PublicVenueDetailDto(
            venue.Id,
            venue.Name,
            venue.Slug,
            venue.Address,
            venue.Latitude,
            venue.Longitude,
            venue.PhotoPlaceholder ?? "placeholder",
            venue.HoldMinutes,
            venue.FreeCancelHours,
            venue.AdvanceType,
            venue.AdvanceValue,
            hasOnlinePayment,
            courts,
            amenities));
    }

    [HttpGet("venues/{slug}/availability")]
    public async Task<IActionResult> Availability(string slug,[FromQuery]DateOnly? date,CancellationToken ct)
    {
        if(date is null)throw new Futsal.Api.Identity.ApiException(400,"VALIDATION_FAILED","date is required in YYYY-MM-DD format.");
        var day=date.Value;
        var venue=await db.Venues.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(v=>v.Slug==slug&&v.IsPublic&&v.IsActive&&db.Locations.Any(l=>l.Id==v.LocationId&&l.IsActive),ct);
        if(venue is null)return NotFound();
        var zone=TimeZoneInfo.FindSystemTimeZoneById(venue.Timezone);var weekday=(short)day.DayOfWeek;
        var courts=await db.Courts.IgnoreQueryFilters().Where(c=>c.VenueId==venue.Id&&c.IsActive).ToListAsync(ct);
        var output=new List<AvailabilitySlot>();
        foreach(var court in courts)
        {
            var hours=await db.CourtHours.AsNoTracking().SingleOrDefaultAsync(x=>x.CourtId==court.Id&&x.Weekday==weekday,ct);if(hours is null)continue;
            // TimeOnly values are wall times; construct them in the venue zone before converting to UTC.
            var openUtc=new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(day.ToDateTime(hours.OpenTime),DateTimeKind.Unspecified),zone),TimeSpan.Zero);
            var closeUtc=new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(day.ToDateTime(hours.CloseTime),DateTimeKind.Unspecified),zone),TimeSpan.Zero);
            var bookings=await db.Bookings.IgnoreQueryFilters().Where(b=>b.CourtId==court.Id&&b.StartAt<closeUtc&&b.EndAt>openUtc&&b.Status!=BookingStatus.CANCELLED&&b.Status!=BookingStatus.EXPIRED)
                .Select(b=>new{b.StartAt,b.EndAt,b.Status,b.HoldExpiresAt}).ToListAsync(ct);
            var blocks=await db.BlockedPeriods.IgnoreQueryFilters().Where(b=>b.CourtId==court.Id&&b.StartAt<closeUtc&&b.EndAt>openUtc).Select(b=>new{b.StartAt,b.EndAt}).ToListAsync(ct);
            for(var start=openUtc;start.AddMinutes(court.SlotMinutes)<=closeUtc;start=start.AddMinutes(court.SlotMinutes))
            {
                var end=start.AddMinutes(court.SlotMinutes);var status=blocks.Any(b=>b.StartAt<end&&b.EndAt>start)?"BLOCKED":"FREE";
                var booking=bookings.FirstOrDefault(b=>b.StartAt<end&&b.EndAt>start&&(b.Status!=BookingStatus.HELD||b.HoldExpiresAt>clock.UtcNow));
                if(booking is not null)status=booking.Status==BookingStatus.HELD?"HELD":"BOOKED";
                var price=await pricing.CalculateAsync(court,start,end,venue,null,ct);
                output.Add(new AvailabilitySlot(court.Id,court.Name,start,end,status,price.Total));
            }
        }
        return Ok(output);
    }

    [HttpGet("venues/{slug}/availability/version")]
    public async Task<IActionResult> AvailabilityVersion(string slug,[FromQuery]DateOnly? date,CancellationToken ct)
    {
        if(date is null)throw new Futsal.Api.Identity.ApiException(400,"VALIDATION_FAILED","date is required in YYYY-MM-DD format.");
        var venue=await db.Venues.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(v=>v.Slug==slug&&v.IsPublic&&v.IsActive&&db.Locations.Any(l=>l.Id==v.LocationId&&l.IsActive),ct);
        if(venue is null)return NotFound();
        var zone=TimeZoneInfo.FindSystemTimeZoneById(venue.Timezone);var localStart=date.Value.ToDateTime(TimeOnly.MinValue,DateTimeKind.Unspecified);var localEnd=date.Value.AddDays(1).ToDateTime(TimeOnly.MinValue,DateTimeKind.Unspecified);
        var start=new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localStart,zone),TimeSpan.Zero);var end=new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localEnd,zone),TimeSpan.Zero);
        var courts=await db.Courts.IgnoreQueryFilters().Where(c=>c.VenueId==venue.Id&&c.IsActive).Select(c=>c.Id).ToArrayAsync(ct);
        var bookings=await db.Bookings.IgnoreQueryFilters().AsNoTracking().Where(b=>courts.Contains(b.CourtId)&&b.StartAt<end&&b.EndAt>start&&b.Status!=BookingStatus.CANCELLED&&b.Status!=BookingStatus.EXPIRED)
            .OrderBy(b=>b.CourtId).ThenBy(b=>b.StartAt).Select(b=>new{b.Id,b.CourtId,b.StartAt,b.EndAt,b.Status,b.HoldExpiresAt,b.UpdatedAt}).ToListAsync(ct);
        var blocks=await db.BlockedPeriods.IgnoreQueryFilters().AsNoTracking().Where(b=>courts.Contains(b.CourtId)&&b.StartAt<end&&b.EndAt>start)
            .OrderBy(b=>b.CourtId).ThenBy(b=>b.StartAt).Select(b=>new{b.Id,b.CourtId,b.StartAt,b.EndAt}).ToListAsync(ct);
        var versionBookings=bookings.Select(b=>new{b.Id,b.CourtId,b.StartAt,b.EndAt,Status=b.Status==BookingStatus.HELD&&b.HoldExpiresAt<=clock.UtcNow?BookingStatus.EXPIRED:b.Status,b.HoldExpiresAt,b.UpdatedAt});
        var stamp=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{bookings=versionBookings,blocks})));
        return Ok(new{date=date.Value,version=stamp});
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
