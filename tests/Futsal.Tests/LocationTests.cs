using Futsal.Api.Controllers;
using Futsal.Api.Bookings;
using Futsal.Api.Locations;
using Futsal.Application.Abstractions;
using Futsal.Domain.Identity;
using Futsal.Domain.Locations;
using Futsal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Futsal.Tests;

public sealed class LocationTests
{
    [Fact]
    public async Task CityVenueListing_IncludesChildAreas()
    {
        await using var f = await Fixture.Create(); await LocationCatalog.SeedAsync(f.Db, default);
        var city = await f.Db.Locations.SingleAsync(x=>x.Slug=="kathmandu"); var area=await f.Db.Locations.SingleAsync(x=>x.Slug=="baneshwor");
        await f.AddVenue(city, city); await f.AddVenue(area, city);
        var result=await new PublicLocationsController(f.Db,f.Clock,new BookingPricing(f.Db)).Venues("kathmandu",default) as OkObjectResult;
        Assert.Equal(2, ((IEnumerable<object>)result!.Value!).Count());
    }

    [Fact]
    public async Task LocationSearch_MatchesEnglishAndNepali()
    {
        await using var f = await Fixture.Create(); await LocationCatalog.SeedAsync(f.Db, default);
        var api=new PublicLocationsController(f.Db,f.Clock,new BookingPricing(f.Db));
        var en=await api.Search("Kathmandu",default) as OkObjectResult;
        var ne=await api.Search("काठमाडौं",default) as OkObjectResult;
        Assert.NotEmpty((IEnumerable<object>)en!.Value!); Assert.NotEmpty((IEnumerable<object>)ne!.Value!);
    }

    [Fact]
    public async Task NearFilter_ReturnsOnlyRadiusAndSortsByDistance()
    {
        await using var f=await Fixture.Create(); await LocationCatalog.SeedAsync(f.Db,default);
        var city=await f.Db.Locations.SingleAsync(x=>x.Slug=="kathmandu");
        await f.AddVenue(city,city,27.7000m,85.3330m,"B"); await f.AddVenue(city,city,27.7100m,85.3330m,"A"); await f.AddVenue(city,city,28.0m,85.3330m,"Far");
        var result=await new PublicLocationsController(f.Db,f.Clock,new BookingPricing(f.Db)).Venues(null,null,"27.7000,85.3330",5,null,null,null,null,"distance",default) as OkObjectResult;
        var rows=((IEnumerable<PublicVenueResult>)result!.Value!).ToArray();
        Assert.Equal(2,rows.Length);Assert.True(rows[0].DistanceKm < rows[1].DistanceKm);
    }

    [Fact]
    public async Task OwnerMayCreateAreaOnlyUnderCity()
    {
        await using var f=await Fixture.Create("Owner"); await LocationCatalog.SeedAsync(f.Db,default);
        var city=await f.Db.Locations.SingleAsync(x=>x.Slug=="kathmandu"); var org=new Organization{Name="Org"};var user=new AppUser{FullName="Owner"};
        f.Db.AddRange(org,user,new Membership{OrganizationId=org.Id,UserId=user.Id,Role=OrganizationRole.OWNER});await f.Db.SaveChangesAsync();f.Current.Set(user.Id,org.Id,"Owner",true);
        await f.AddVenue(city,city,org:org);
        var api=new LocationsAdminController(f.Db,f.Current);
        var allowed=await api.Create(new LocationWrite("New Area","नयाँ क्षेत्र",LocationType.AREA,city.Id,null,null),default);
        var forbidden=await api.Create(new LocationWrite("New City","नयाँ शहर",LocationType.CITY,city.ParentId,null,null),default);
        Assert.IsType<CreatedResult>(allowed); Assert.IsType<ForbidResult>(forbidden);
    }

    [Fact]
    public async Task InactiveLocationsAndPrivateVenuesAreHidden()
    {
        await using var f=await Fixture.Create(); await LocationCatalog.SeedAsync(f.Db,default);
        var city=await f.Db.Locations.SingleAsync(x=>x.Slug=="kathmandu"); var inactive=await f.Db.Locations.SingleAsync(x=>x.Slug=="pokhara");f.Current.SetSuperAdmin();inactive.IsActive=false;await f.Db.SaveChangesAsync();f.Current.SetSuperAdmin(false);
        await f.AddVenue(city,city,name:"Public"); await f.AddVenue(city,city,name:"Private",isPublic:false); await f.AddVenue(inactive,inactive,name:"Inactive");
        var result=await new PublicLocationsController(f.Db,f.Clock,new BookingPricing(f.Db)).Venues(null,null,null,null,null,null,null,null,null,default) as OkObjectResult;
        Assert.Single((IEnumerable<object>)result!.Value!);
    }

    [Fact]
    public async Task TenantCannotChangeAnotherOrganizationsVenueLocation()
    {
        await using var f=await Fixture.Create(); await LocationCatalog.SeedAsync(f.Db,default);
        var city=await f.Db.Locations.SingleAsync(x=>x.Slug=="kathmandu");var orgA=new Organization{Name="A"};var orgB=new Organization{Name="B"};var user=new AppUser{FullName="A"};
        f.Db.AddRange(orgA,orgB,user,new Membership{OrganizationId=orgA.Id,UserId=user.Id,Role=OrganizationRole.OWNER});await f.Db.SaveChangesAsync();
        var venue=await f.AddVenue(city,city,org:orgB);f.Current.Set(user.Id,orgA.Id,"Owner",true);
        venue=await f.Db.Venues.IgnoreQueryFilters().SingleAsync(x=>x.Id==venue.Id);
        venue.LocationId=(await f.Db.Locations.SingleAsync(x=>x.Slug=="pokhara")).Id;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Db.SaveChangesAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public FutsalDbContext Db {get;} public Current Current {get;} public Clock Clock {get;}=new();
        private Fixture(string role){Current=new Current();Db=new FutsalDbContext(new DbContextOptionsBuilder<FutsalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,Current,Clock);}
        public static async Task<Fixture> Create(string role=""){var f=new Fixture(role);await f.Db.Database.EnsureCreatedAsync();return f;}
        public async Task<Venue> AddVenue(Location location,Location city,decimal? lat=27.7m,decimal? lng=85.333m,string name="Venue",Organization? org=null,bool isPublic=true){org??=new Organization{Name="Venue Org"}; if(!await Db.Organizations.IgnoreQueryFilters().AnyAsync(x=>x.Id==org.Id))Db.Organizations.Add(org);var v=new Venue{OrganizationId=org.Id,LocationId=location.Id,CityId=city.Id,Name=name,Slug=Guid.NewGuid().ToString("N"),Latitude=lat,Longitude=lng,IsPublic=isPublic};Db.Venues.Add(v);Db.Courts.Add(new Court{OrganizationId=org.Id,VenueId=v.Id,Name="Court 1",PricePerSlot=1200});await Db.SaveChangesAsync();return v;}
        public ValueTask DisposeAsync()=>Db.DisposeAsync();
    }
    private sealed class Current : ICurrentUser {public Guid? UserId{get;private set;} public Guid? OrganizationId{get;private set;} public string? Role{get;private set;}public bool IsSuperAdmin{get;private set;}public bool IsAuthenticated{get;private set;}public void Set(Guid? u,Guid? o,string? r,bool a){UserId=u;OrganizationId=o;Role=r;IsAuthenticated=a;}public void SetSuperAdmin(bool value=true)=>IsSuperAdmin=value;}
    private sealed class Clock : IClock {public DateTimeOffset UtcNow=>DateTimeOffset.UtcNow;}
}
