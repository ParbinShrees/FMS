using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Futsal.Api.Controllers;
using Futsal.Api.Identity;
using Futsal.Application.Abstractions;
using Futsal.Contracts;
using Futsal.Domain.Bookings;
using Futsal.Domain.Identity;
using Futsal.Domain.Locations;
using Futsal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace Futsal.Tests;
public sealed class SchedulingHttpTests(Milestone8PostgresFixture fixture):IClassFixture<Milestone8PostgresFixture>
{
 sealed class Sender:INotificationSender{public Task SendAsync(NotificationMessage message,CancellationToken ct)=>Task.CompletedTask;}
 [Fact]public async Task Real_cancellation_event_offers_the_FIFO_slot_and_HTTP_roles_and_ownership_are_enforced()
 {
  var now=DateTimeOffset.Parse("2026-10-30T00:00:00Z");var start=DateTimeOffset.Parse("2026-10-30T13:15:00Z");
  await using var factory=new WebApplicationFactory<AuthController>().WithWebHostBuilder(b=>b.UseEnvironment("Development").ConfigureServices(services=>{services.RemoveAll<DbContextOptions<FutsalDbContext>>();services.RemoveAll<Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration<FutsalDbContext>>();services.AddDbContext<FutsalDbContext>(o=>o.UseNpgsql(fixture.Container.GetConnectionString(),FutsalDbContext.ConfigurePostgres));services.RemoveAll<IClock>();services.AddSingleton<IClock>(new FixedClock(now));services.RemoveAll<INotificationSender>();services.AddSingleton<INotificationSender>(new Sender());}).ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:FutsalPro",fixture.Container.GetConnectionString()},{"Jwt:SigningKey",Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48))},{"Seed:SuperAdminPassword",null},{"Seed:OwnerPassword",null},{"PublicBooking:BaseUrl","https://pilot.example.invalid"}})));
  using var client=factory.CreateClient(new(){BaseAddress=new Uri("https://localhost"),AllowAutoRedirect=false});await client.GetAsync("/health/live");
  var org=new Organization{Name="Event arena",CreatedAt=now};var otherOrg=new Organization{Name="Other",CreatedAt=now};var owner=new AppUser{FullName="Owner"};var cashier=new AppUser{FullName="Cashier"};var player=new AppUser{FullName="Private player",Phone="+9779800000091"};var other=new AppUser{FullName="Other player",Phone="+9779800000092"};var customer=new Customer{OrganizationId=org.Id,UserId=player.Id,FullName=player.FullName,Phone=player.Phone,CreatedAt=now};var city=new Location{Type=LocationType.CITY,NameEn="City",Slug=Guid.NewGuid().ToString(),IsSeeded=true};Venue venue;Court court;
  await using(var db=new FutsalDbContext(fixture.Options,new SuperAdminUser(),new FixedClock(now))){db.Organizations.AddRange(org,otherOrg);db.Users.AddRange(owner,cashier,player,other);db.Locations.Add(city);await db.SaveChangesAsync();venue=new(){OrganizationId=org.Id,LocationId=city.Id,CityId=city.Id,Name="Event arena",Slug="events-"+Guid.NewGuid().ToString("N")};court=new(){OrganizationId=org.Id,VenueId=venue.Id,Name="One",PricePerSlot=1000};db.Venues.Add(venue);db.Courts.Add(court);db.Customers.Add(customer);db.Subscriptions.Add(new(){OrganizationId=org.Id,Status="ACTIVE",StartsAt=now.AddDays(-1),EndsAt=now.AddYears(1)});db.Memberships.AddRange(new Membership{OrganizationId=org.Id,UserId=owner.Id,Role=OrganizationRole.OWNER},new Membership{OrganizationId=org.Id,UserId=cashier.Id,Role=OrganizationRole.CASHIER});for(short d=0;d<7;d++)db.CourtHours.Add(new(){CourtId=court.Id,Weekday=d,OpenTime=new(6,0),CloseTime=new(23,0)});await db.SaveChangesAsync();}
  using var scope=factory.Services.CreateScope();var validation=scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>>().Get("Bearer").TokenValidationParameters;var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Jwt:SigningKey",System.Text.Encoding.UTF8.GetString(((Microsoft.IdentityModel.Tokens.SymmetricSecurityKey)validation.IssuerSigningKey!).Key)},{"Jwt:Issuer",validation.ValidIssuer},{"Jwt:Audience",validation.ValidAudience}}).Build();var tokens=new JwtTokenService(config,new FixedClock(DateTimeOffset.UtcNow));
  void Login(AppUser user,Guid? organization,string role)=>client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",tokens.CreateAccessToken(user,organization,role).AccessToken);
  client.DefaultRequestHeaders.Add("Idempotency-Key",Guid.NewGuid().ToString());Login(owner,org.Id,"Owner");var booking=await client.PostAsJsonAsync("/api/v1/staff/bookings",new{courtId=court.Id,phone="+9779800000093",fullName="Booked customer",startAt=start,endAt=start.AddHours(1)});Assert.Equal(HttpStatusCode.Created,booking.StatusCode);var normal=(await booking.Content.ReadFromJsonAsync<BookingDto>())!;
  Login(player,null,"Customer");var joined=await client.PostAsJsonAsync("/api/v1/waitlist",new WaitlistJoin(venue.Id,court.Id,new(2026,10,30),new(19,0),new(20,0),1));Assert.Equal(HttpStatusCode.OK,joined.StatusCode);var entry=(await joined.Content.ReadFromJsonAsync<WaitlistView>())!;
  Login(cashier,org.Id,"Cashier");Assert.Equal(HttpStatusCode.Forbidden,(await client.DeleteAsync("/api/v1/staff/recurring/"+Guid.NewGuid())).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await client.PostAsJsonAsync($"/api/v1/staff/waitlist/{entry.Id}/approve",new{})).StatusCode);
  Login(owner,org.Id,"Owner");Assert.Equal(HttpStatusCode.NoContent,(await client.PostAsJsonAsync($"/api/v1/staff/bookings/{normal.Id}/cancel",new{reason="Released"})).StatusCode);
  Login(player,null,"Customer");var rows=(await client.GetFromJsonAsync<List<WaitlistView>>("/api/v1/waitlist/mine"))!;Assert.Equal("OFFERED",Assert.Single(rows).Status);Assert.Null(rows[0].Customer);Assert.Null(rows[0].Phone);var claim=await client.PostAsJsonAsync($"/api/v1/waitlist/{entry.Id}/claim",new{});Assert.Equal(HttpStatusCode.OK,claim.StatusCode);Assert.Equal("HELD",(await claim.Content.ReadFromJsonAsync<BookingDto>())!.Status);
  Login(other,null,"Customer");Assert.Equal(HttpStatusCode.NotFound,(await client.PostAsJsonAsync($"/api/v1/waitlist/{entry.Id}/claim",new{})).StatusCode);Assert.Empty((await client.GetFromJsonAsync<List<WaitlistView>>("/api/v1/waitlist/mine"))!);
  await using var verify=new FutsalDbContext(fixture.Options,new SuperAdminUser(),new FixedClock(now));Assert.Equal(1,await verify.Notifications.CountAsync(x=>x.VenueId==venue.Id&&x.Template=="waitlist_offer"));Assert.Equal(1,await verify.Bookings.CountAsync(x=>x.CourtId==court.Id&&x.Status==BookingStatus.HELD));
 }
}
