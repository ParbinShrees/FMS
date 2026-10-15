using Futsal.Api.Bookings;
using Futsal.Api.Contracts;
using Futsal.Api.Identity;
using Futsal.Application.Abstractions;
using Futsal.Domain.Bookings;
using Futsal.Domain.Identity;
using Futsal.Domain.Locations;
using Futsal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
namespace Futsal.Tests;
public sealed class LaunchConcurrencyTests(Milestone8PostgresFixture fixture):IClassFixture<Milestone8PostgresFixture>
{
 private static readonly DateTimeOffset Now=DateTimeOffset.Parse("2026-10-15T00:00:00Z");
 private static readonly DateTimeOffset Start=DateTimeOffset.Parse("2026-10-16T13:15:00Z");
 private sealed class User(Guid id,Guid? org=null,string role="Customer"):ICurrentUser{public Guid? UserId=>id;public Guid? OrganizationId=>org;public string? Role=>role;public bool IsAuthenticated=>true;public bool IsSuperAdmin=>false;}
 private FutsalDbContext Db(ICurrentUser? user=null)=>new(fixture.Options,user??new SuperAdminUser(),new FixedClock(Now));
 private static BookingService Service(FutsalDbContext db,ICurrentUser user)=>new(db,user,new FixedClock(Now),new BookingPricing(db),new NoopBookingNotifier(),NullLogger<BookingService>.Instance);
 private static int seedBatch;
 private async Task<(Guid Org,Guid Venue,Guid Court,Guid Owner,Guid[] Players)> Seed()
 {
  await using var db=Db();var org=new Organization{Name="Race arena",CreatedAt=Now};var city=new Location{Type=LocationType.CITY,NameEn="Kathmandu",Slug=Guid.NewGuid().ToString(),IsSeeded=true};db.Organizations.Add(org);db.Locations.Add(city);await db.SaveChangesAsync();
  var venue=new Venue{OrganizationId=org.Id,LocationId=city.Id,CityId=city.Id,Name="Race arena",Slug=Guid.NewGuid().ToString()};db.Venues.Add(venue);var court=new Court{OrganizationId=org.Id,VenueId=venue.Id,Name="Race court",PricePerSlot=1000};db.Courts.Add(court);var owner=new AppUser{FullName="Owner",CreatedAt=Now};db.Users.Add(owner);db.Memberships.Add(new(){OrganizationId=org.Id,UserId=owner.Id,Role=OrganizationRole.OWNER});db.Subscriptions.Add(new(){OrganizationId=org.Id,StartsAt=Now.AddDays(-1),EndsAt=Now.AddDays(30),Status="ACTIVE",MaxCourts=5});
  var batch=Interlocked.Increment(ref seedBatch);var players=Enumerable.Range(0,50).Select(i=>new AppUser{FullName="Player",Phone="+97798"+(10000000+batch*1000+i).ToString(),CreatedAt=Now}).ToArray();db.Users.AddRange(players);db.VenuePaymentSettings.Add(new(){OrganizationId=org.Id,VenueId=venue.Id,IsEnabled=true,MerchantCode="sandbox",EncryptedSecret="not-a-live-credential"});for(short i=0;i<7;i++)db.CourtHours.Add(new(){CourtId=court.Id,Weekday=i,OpenTime=new(6,0),CloseTime=new(23,0)});await db.SaveChangesAsync();return(org.Id,venue.Id,court.Id,owner.Id,players.Select(x=>x.Id).ToArray());
 }
 [Fact]public async Task Fifty_parallel_holds_yield_one_success_and_fortynine_conflicts()
 {
  var seed=await Seed();var outcomes=await Task.WhenAll(seed.Players.Select(async id=>{var user=new User(id);await using var db=Db(user);try{await Service(db,user).CreateHoldAsync(new(seed.Court,Start,Start.AddHours(1)),Guid.NewGuid().ToString(),null,default);return "OK";}catch(ApiException ex){return ex.Code;}}));
  Assert.Equal(1,outcomes.Count(x=>x=="OK"));Assert.Equal(49,outcomes.Count(x=>x=="SLOT_UNAVAILABLE"));await using var verify=Db();Assert.Equal(1,await verify.Bookings.CountAsync(x=>x.CourtId==seed.Court&&x.Status==BookingStatus.HELD));
 }
 [Fact]public async Task Parallel_reschedules_cannot_overlap()
 {
  var seed=await Seed();var owner=new User(seed.Owner,seed.Org,"Owner");Guid a,b;
  await using(var db=Db(owner)){var service=Service(db,owner);a=(await service.CreateStaffBookingAsync(new(seed.Court,"+9779800000011","A",Start,Start.AddHours(1)),Guid.NewGuid().ToString(),null,default)).Id;b=(await service.CreateStaffBookingAsync(new(seed.Court,"+9779800000012","B",Start.AddHours(1),Start.AddHours(2)),Guid.NewGuid().ToString(),null,default)).Id;}
  var outcomes=await Task.WhenAll(new[]{a,b}.Select(async id=>{await using var db=Db(owner);try{await Service(db,owner).RescheduleAsync(id,new(Start.AddHours(2),Start.AddHours(3)),null,default);return "OK";}catch(ApiException ex){return ex.Code;}}));Assert.Single(outcomes,x=>x=="OK");Assert.Single(outcomes,x=>x=="SLOT_UNAVAILABLE");
 }
 [Fact]public async Task Cancel_and_rebook_race_leaves_at_most_one_active_booking()
 {
  var seed=await Seed();var owner=new User(seed.Owner,seed.Org,"Owner");Guid original;await using(var db=Db(owner))original=(await Service(db,owner).CreateStaffBookingAsync(new(seed.Court,"+9779800000013","A",Start,Start.AddHours(1)),Guid.NewGuid().ToString(),null,default)).Id;
  var cancel=Task.Run(async()=>{await using var db=Db(owner);await Service(db,owner).CancelStaffAsync(original,"Race cancellation",null,default);});
  var create=Task.Run(async()=>{var player=new User(seed.Players[0]);await using var db=Db(player);try{await Service(db,player).CreateHoldAsync(new(seed.Court,Start,Start.AddHours(1)),Guid.NewGuid().ToString(),null,default);}catch(ApiException ex){Assert.Equal("SLOT_UNAVAILABLE",ex.Code);}});await Task.WhenAll(cancel,create);
  await using var verify=Db();Assert.True(await verify.Bookings.CountAsync(x=>x.CourtId==seed.Court&&x.Status!=BookingStatus.CANCELLED&&x.Status!=BookingStatus.EXPIRED)<=1);
 }
 [Fact]public async Task Confirm_after_expiry_racing_new_booking_has_database_overlap_protection()
 {
  var seed=await Seed();Guid id;await using(var db=Db()){var held=Booking.NewHeld();held.OrganizationId=seed.Org;held.VenueId=seed.Venue;held.CourtId=seed.Court;held.BookingCode=Guid.NewGuid().ToString();held.StartAt=Start;held.EndAt=Start.AddHours(1);held.CreatedAt=Now.AddHours(-1);held.UpdatedAt=Now;held.HoldExpiresAt=Now.AddMinutes(-1);held.Expire();db.Bookings.Add(held);await db.SaveChangesAsync();id=held.Id;}
  var confirm=Task.Run(async()=>{await using var db=Db();var b=await db.Bookings.SingleAsync(x=>x.Id==id);b.ConfirmLate();try{await db.SaveChangesAsync();}catch(DbUpdateException ex){Assert.Equal("23P01",((Npgsql.PostgresException)ex.InnerException!).SqlState);}});
  var book=Task.Run(async()=>{var player=new User(seed.Players[0]);await using var db=Db(player);try{await Service(db,player).CreateHoldAsync(new(seed.Court,Start,Start.AddHours(1)),Guid.NewGuid().ToString(),null,default);}catch(ApiException ex){Assert.Equal("SLOT_UNAVAILABLE",ex.Code);}});await Task.WhenAll(confirm,book);await using var verify=Db();Assert.Equal(1,await verify.Bookings.CountAsync(x=>x.CourtId==seed.Court&&(x.Status==BookingStatus.HELD||x.Status==BookingStatus.CONFIRMED)));
 }
 [Fact]public async Task Subscription_expiry_blocks_new_bookings_without_deleting_records()
 {
  var seed=await Seed();await using(var db=Db()){var license=await db.Subscriptions.SingleAsync(x=>x.OrganizationId==seed.Org);license.EndsAt=Now.AddDays(-8);license.Status="PAST_DUE";await db.SaveChangesAsync();}
  var user=new User(seed.Players[0]);await using(var db=Db(user)){var ex=await Assert.ThrowsAsync<ApiException>(()=>Service(db,user).CreateHoldAsync(new(seed.Court,Start,Start.AddHours(1)),Guid.NewGuid().ToString(),null,default));Assert.Equal("SUBSCRIPTION_INACTIVE",ex.Code);}
  await using(var db=Db()){Assert.True(await db.Courts.AnyAsync(x=>x.Id==seed.Court));Assert.True(await db.Organizations.AnyAsync(x=>x.Id==seed.Org));}
 }
 [Fact]public async Task Concurrent_court_additions_obey_subscription_cap()
 {
  var seed=await Seed();await using(var db=Db()){var license=await db.Subscriptions.SingleAsync(x=>x.OrganizationId==seed.Org);license.MaxCourts=2;await db.SaveChangesAsync();}
  var outcomes=await Task.WhenAll(Enumerable.Range(0,2).Select(async _=>{await using var db=Db(new User(seed.Owner,seed.Org,"Owner"));db.Courts.Add(new(){OrganizationId=seed.Org,VenueId=seed.Venue,Name="Extra",PricePerSlot=1000});try{await db.SaveChangesAsync();return "OK";}catch(BookingDomainException ex){return ex.Code;}}));
  Assert.Single(outcomes,x=>x=="OK");Assert.Single(outcomes,x=>x=="COURT_LIMIT_REACHED");
 }
 [Fact]public async Task Database_restart_preserves_committed_booking_and_next_requests_recover()
 {
  var seed=await Seed();var user=new User(seed.Players[0]);Guid id;
  await using(var db=Db(user))id=(await Service(db,user).CreateHoldAsync(new(seed.Court,Start,Start.AddHours(1)),Guid.NewGuid().ToString(),null,default)).Id;
  var restart=new System.Diagnostics.ProcessStartInfo("docker"){RedirectStandardOutput=true,RedirectStandardError=true};restart.ArgumentList.Add("restart");restart.ArgumentList.Add(fixture.Container.Id);using(var process=System.Diagnostics.Process.Start(restart)!){await process.WaitForExitAsync();Assert.Equal(0,process.ExitCode);}
  var inspect=new System.Diagnostics.ProcessStartInfo("docker"){RedirectStandardOutput=true};inspect.ArgumentList.Add("port");inspect.ArgumentList.Add(fixture.Container.Id);inspect.ArgumentList.Add("5432/tcp");string published;using(var process=System.Diagnostics.Process.Start(inspect)!){published=await process.StandardOutput.ReadToEndAsync();await process.WaitForExitAsync();Assert.Equal(0,process.ExitCode);}
  var port=int.Parse(published.Split('\n')[0].Trim().Split(':').Last());
  var cs=new Npgsql.NpgsqlConnectionStringBuilder(fixture.Container.GetConnectionString()){Port=port,Timeout=2,CommandTimeout=5};
  var options=new DbContextOptionsBuilder<FutsalDbContext>().UseNpgsql(cs.ConnectionString,FutsalDbContext.ConfigurePostgres).Options;
  fixture.UpdateConnectionString(cs.ConnectionString);
  using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
  for(;;){await using var probe=new FutsalDbContext(options,new SuperAdminUser(),new FixedClock(Now));if(await probe.Database.CanConnectAsync(timeout.Token))break;await Task.Delay(250,timeout.Token);}
  await using(var db=new FutsalDbContext(options,new SuperAdminUser(),new FixedClock(Now))){Assert.Equal(id,(await db.Bookings.SingleAsync(x=>x.CourtId==seed.Court)).Id);}
  var other=new User(seed.Players[1]);await using var retryDb=new FutsalDbContext(options,other,new FixedClock(Now));var ex=await Assert.ThrowsAsync<ApiException>(()=>Service(retryDb,other).CreateHoldAsync(new(seed.Court,Start,Start.AddHours(1)),Guid.NewGuid().ToString(),null,default));Assert.Equal("SLOT_UNAVAILABLE",ex.Code);

 }

}
