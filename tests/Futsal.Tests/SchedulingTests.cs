using System.Text.Json;
using Futsal.Api.Bookings;
using Futsal.Api.Contracts;
using Futsal.Api.Controllers;
using Futsal.Api.Identity;
using Futsal.Api.Scheduling;
using Futsal.Application.Abstractions;
using Futsal.Application.Scheduling;
using Futsal.Contracts;
using Futsal.Domain.Bookings;
using Futsal.Domain.Identity;
using Futsal.Domain.Locations;
using Futsal.Infrastructure.Notifications;
using Futsal.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
namespace Futsal.Tests;
public sealed class SchedulingTests(Milestone8PostgresFixture fixture):IClassFixture<Milestone8PostgresFixture>
{
 static readonly DateTimeOffset Now=DateTimeOffset.Parse("2026-10-30T00:00:00Z");static readonly DateOnly Day=new(2026,10,30);static readonly DateTimeOffset Start=DateTimeOffset.Parse("2026-10-30T13:15:00Z");
 sealed class Clock:IClock{public DateTimeOffset UtcNow{get;set;}=Now;}
 sealed class User(Guid id,Guid? org=null,string role="Owner"):ICurrentUser{public Guid? UserId=>id;public Guid? OrganizationId=>org;public string? Role=>role;public bool IsAuthenticated=>true;public bool IsSuperAdmin=>false;}
 sealed class Events:IBookingNotifier{public List<BookingEvent> Messages=[];public Task PublishAsync(BookingEvent e,CancellationToken ct){Messages.Add(e);return Task.CompletedTask;}}
 sealed class Sender:INotificationSender{public List<NotificationMessage> Messages=[];public Task SendAsync(NotificationMessage m,CancellationToken ct){Messages.Add(m);return Task.CompletedTask;}}
 sealed record SeedData(Guid Org,Guid Owner,Venue Venue,Court Court,Customer[] Customers,AppUser[] Players);
 static int batch;
 FutsalDbContext Db(ICurrentUser? user=null,IClock? clock=null)=>new(fixture.Options,user??new SuperAdminUser(),clock??new Clock());
 SchedulingWriter Writer(FutsalDbContext db,IClock clock)=>new(db,clock,new(db));
 RecurringService Series(FutsalDbContext db,ICurrentUser user,IClock? clock=null,Events? events=null)=>new(db,user,clock??new Clock(),Writer(db,clock??new Clock()),events??new());
 WaitlistService Wait(FutsalDbContext db,ICurrentUser user,IClock? clock=null,Events? events=null,NotificationQueue? queue=null)=>new(db,user,clock??new Clock(),Writer(db,clock??new Clock()),events??new(),queue,"https://pilot.example.invalid");
 BookingService Books(FutsalDbContext db,ICurrentUser user,IClock? clock=null)=>new(db,user,clock??new Clock(),new(db),new NoopBookingNotifier(),NullLogger<BookingService>.Instance);
 async Task<SeedData> Seed()
 {
  await using var db=Db();var org=new Organization{Name="Scheduling",CreatedAt=Now};var owner=new AppUser{FullName="Owner",CreatedAt=Now};var city=new Location{Type=LocationType.CITY,NameEn="City",Slug=Guid.NewGuid().ToString(),IsSeeded=true};db.Organizations.Add(org);db.Users.Add(owner);db.Locations.Add(city);await db.SaveChangesAsync();var venue=new Venue{OrganizationId=org.Id,Name="Weekly arena",Slug=Guid.NewGuid().ToString(),CityId=city.Id,LocationId=city.Id};db.Venues.Add(venue);var court=new Court{OrganizationId=org.Id,VenueId=venue.Id,Name="Court 1",PricePerSlot=1000};db.Courts.Add(court);db.Memberships.Add(new(){OrganizationId=org.Id,UserId=owner.Id,Role=OrganizationRole.OWNER});db.Subscriptions.Add(new(){OrganizationId=org.Id,Status="ACTIVE",StartsAt=Now.AddDays(-1),EndsAt=Now.AddYears(2),MaxCourts=5});db.VenuePaymentSettings.Add(new(){OrganizationId=org.Id,VenueId=venue.Id,IsEnabled=true,MerchantCode="sandbox",EncryptedSecret="not-live"});
  var n=Interlocked.Increment(ref batch);var players=Enumerable.Range(0,5).Select(i=>new AppUser{FullName="Private player "+i,Phone="+97798"+(20000000+n*10+i),CreatedAt=Now}).ToArray();db.Users.AddRange(players);var customers=players.Select(p=>new Customer{OrganizationId=org.Id,UserId=p.Id,FullName=p.FullName,Phone=p.Phone!,CreatedAt=Now,IsRegular=true,DiscountPercent=10}).ToArray();db.Customers.AddRange(customers);for(short d=0;d<7;d++)db.CourtHours.Add(new(){CourtId=court.Id,Weekday=d,OpenTime=TimeOnly.MinValue,CloseTime=new(23,59)});await db.SaveChangesAsync();return new(org.Id,owner.Id,venue,court,customers,players);
 }
 SeriesWrite Request(SeedData s,string mode="CURRENT")=>new(s.Court.Id,s.Customers[0].Id,5,new(19,0),1,Day,null,mode);
 async Task<Guid> Create(SeedData seed,string mode="CURRENT",Clock? clock=null){var user=new User(seed.Owner,seed.Org);await using var db=Db(user,clock);return(await Series(db,user,clock).CreateAsync(new(Request(seed,mode),true),Guid.NewGuid().ToString(),default)).Id!.Value;}
 async Task<Guid> Book(SeedData s,DateTimeOffset? start=null){var user=new User(s.Owner,s.Org);await using var db=Db(user);return(await Books(db,user).CreateStaffBookingAsync(new(s.Court.Id,s.Customers[1].Phone,s.Customers[1].FullName,start??Start,(start??Start).AddHours(1)),Guid.NewGuid().ToString(),null,default)).Id;}
 async Task Cancel(SeedData s,Guid id,IClock? clock=null){var user=new User(s.Owner,s.Org);await using var db=Db(user,clock);await Books(db,user,clock).CancelStaffAsync(id,"Freed",null,default);}
 async Task<WaitlistView> Join(SeedData s,int player,DateOnly? day=null){var user=new User(s.Players[player].Id,null,"Customer");await using var db=Db(user);return await Wait(db,user).JoinAsync(new(s.Venue.Id,s.Court.Id,day??Day,new(19,0),new(20,0),1),Guid.NewGuid().ToString(),false,default);}
 [Fact]public async Task Preview_reports_past_booked_blocked_closed_and_never_forces_conflicts()
 {
  var s=await Seed();await Book(s);await using(var admin=Db()){admin.BlockedPeriods.Add(new(){OrganizationId=s.Org,CourtId=s.Court.Id,StartAt=Start.AddDays(7),EndAt=Start.AddDays(7).AddHours(1),Reason="Maintenance"});await admin.SaveChangesAsync();}
  var user=new User(s.Owner,s.Org);await using var db=Db(user);var service=Series(db,user);var request=Request(s) with{StartDate=Day.AddDays(-7)};var report=await service.PreviewAsync(request,default);Assert.Equal("PAST",report[0].Result);Assert.Equal("BOOKED",report[1].Result);Assert.Equal("BLOCKED",report[2].Result);Assert.Contains(report,x=>x.Result=="FREE");Assert.Null((await service.CreateAsync(new(request,false),Guid.NewGuid().ToString(),default)).Id);Assert.Empty(await db.RecurringSeries.ToListAsync());
  var closed=await service.PreviewAsync(request with{StartTimeLocal=new(23,0),DurationSlots=2},default);Assert.Contains(closed,x=>x.Result=="CLOSED");var created=await service.CreateAsync(new(request,true),Guid.NewGuid().ToString(),default);Assert.NotNull(created.Id);Assert.Equal(6,await db.Bookings.CountAsync(x=>x.SeriesId==created.Id));
 }
 [Fact]public async Task Generation_is_idempotent_rolls_eight_weeks_and_uses_Nepal_month_and_midnight_dates()
 {
  var s=await Seed();var clock=new Clock();var id=await Create(s,clock:clock);await using(var db=Db()){Assert.Equal(8,await db.Bookings.CountAsync(x=>x.SeriesId==id));Assert.Equal(Start,(await db.Bookings.SingleAsync(x=>x.SeriesId==id&&x.SeriesDate==Day)).StartAt);await Series(db,new SuperAdminUser(),clock).GenerateAsync(id,default);await Series(db,new SuperAdminUser(),clock).GenerateAsync(id,default);Assert.Equal(8,await db.Bookings.CountAsync(x=>x.SeriesId==id));}
  clock.UtcNow=Now.AddDays(7);await using(var db=Db(clock:clock)){await Series(db,new SuperAdminUser(),clock).GenerateAsync(id,default);Assert.Equal(9,await db.Bookings.CountAsync(x=>x.SeriesId==id));Assert.Equal(Start.AddDays(56),(await db.Bookings.Where(x=>x.SeriesId==id).OrderByDescending(x=>x.StartAt).FirstAsync()).StartAt);}
  Assert.Equal(DateTimeOffset.Parse("2026-10-31T18:15:00Z"),SeriesCalendar.Utc(new(2026,11,1),TimeOnly.MinValue,"Asia/Kathmandu"));Assert.Equal(DateTimeOffset.Parse("2026-11-06T13:15:00Z"),SeriesCalendar.Utc(new(2026,11,6),new(19,0),"Asia/Kathmandu"));
 }
 [Fact]public async Task Later_generation_conflict_records_staff_alert_without_creating_an_overlap()
 {
  var s=await Seed();var id=await Create(s);await Book(s,Start.AddDays(56));var clock=new Clock{UtcNow=Now.AddDays(7)};await using var db=Db(clock:clock);await Series(db,new SuperAdminUser(),clock).GenerateAsync(id,default);Assert.Equal(8,await db.Bookings.CountAsync(x=>x.SeriesId==id));var conflict=await db.SeriesConflicts.SingleAsync(x=>x.SeriesId==id);Assert.Equal("BOOKED",conflict.Reason);Assert.Null(conflict.ResolvedAt);var user=new User(s.Owner,s.Org);await using var tenant=Db(user,clock);Assert.Equal(1,(await Series(tenant,user,clock).ListAsync(s.Venue.Id,null,default)).Single().Conflicts);
 }
 [Fact]public async Task Locked_and_current_pricing_apply_regular_discount_at_occurrence_creation()
 {
  var a=await Seed();var b=await Seed();var locked=await Create(a,"LOCKED");var current=await Create(b);await using(var db=Db()){var courts=await db.Courts.Where(x=>x.Id==a.Court.Id||x.Id==b.Court.Id).ToListAsync();foreach(var court in courts)court.PricePerSlot=2000;await db.SaveChangesAsync();}var clock=new Clock{UtcNow=Now.AddDays(7)};await using var verify=Db(clock:clock);await Series(verify,new SuperAdminUser(),clock).GenerateAsync(locked,default);await Series(verify,new SuperAdminUser(),clock).GenerateAsync(current,default);Assert.Equal(900,(await verify.Bookings.SingleAsync(x=>x.SeriesId==locked&&x.SeriesDate==Day.AddDays(56))).TotalAmount);Assert.Equal(1800,(await verify.Bookings.SingleAsync(x=>x.SeriesId==current&&x.SeriesDate==Day.AddDays(56))).TotalAmount);Assert.Equal(1800,(await verify.Bookings.SingleAsync(x=>x.SeriesId==current&&x.SeriesDate==Day.AddDays(14))).TotalAmount);Assert.Equal(900,(await verify.Bookings.SingleAsync(x=>x.SeriesId==current&&x.SeriesDate==Day)).TotalAmount);
 }
 [Fact]public async Task Skip_move_pause_resume_end_and_cancel_one_keep_past_bookings()
 {
  var s=await Seed();var id=await Create(s);var user=new User(s.Owner,s.Org);var clock=new Clock();await using(var db=Db(user,clock)){var service=Series(db,user,clock);await service.OccurrenceAsync(id,"skip",new(Day.AddDays(7),"Holiday"),Guid.NewGuid().ToString(),default);await service.OccurrenceAsync(id,"move",new(Day.AddDays(14),"Moved",Start.AddDays(14).AddHours(1)),Guid.NewGuid().ToString(),default);Assert.Equal(Start.AddDays(14).AddHours(1),(await db.Bookings.SingleAsync(x=>x.SeriesId==id&&x.SeriesDate==Day.AddDays(14))).StartAt);await service.StateAsync(id,"pause",new(Day),Guid.NewGuid().ToString(),default);}
  clock.UtcNow=Now.AddDays(7);await using(var db=Db(user,clock)){var service=Series(db,user,clock);await service.GenerateAsync(id,default);Assert.Equal(8,await db.Bookings.CountAsync(x=>x.SeriesId==id));await service.StateAsync(id,"resume",new(Day),Guid.NewGuid().ToString(),default);await service.GenerateAsync(id,default);Assert.Equal(9,await db.Bookings.CountAsync(x=>x.SeriesId==id));await service.OccurrenceAsync(id,"cancel",new(Day.AddDays(21),"Cannot play"),Guid.NewGuid().ToString(),default);await service.StateAsync(id,"end",new(Day.AddDays(28)),Guid.NewGuid().ToString(),default);Assert.Equal(BookingStatus.CONFIRMED,(await db.Bookings.SingleAsync(x=>x.SeriesId==id&&x.SeriesDate==Day)).Status);Assert.DoesNotContain(await db.Bookings.Where(x=>x.SeriesId==id&&x.SeriesDate>=Day.AddDays(28)).ToListAsync(),x=>x.Status==BookingStatus.CONFIRMED);}
 }

 [Fact]public async Task Changing_from_a_date_rolls_back_all_ranges_on_conflict_and_preserves_paid_booking_identity()
 {
  var s=await Seed();var id=await Create(s);var user=new User(s.Owner,s.Org);Guid original;await using(var db=Db(user)){var service=Series(db,user);original=(await db.Bookings.SingleAsync(x=>x.SeriesId==id&&x.SeriesDate==Day.AddDays(7))).Id;await service.PrepayAsync(id,new(2,1500,"CASH"),Guid.NewGuid().ToString(),default);}
  var other=await Book(s,Start.AddDays(14).AddHours(1));await using(var db=Db(user)){var service=Series(db,user);var report=await service.ChangeAsync(id,new(Day.AddDays(7),s.Court.Id,new(20,0),1),Guid.NewGuid().ToString(),default);Assert.Contains(report,x=>x.Result=="BOOKED");Assert.Equal(Start.AddDays(7),(await db.Bookings.SingleAsync(x=>x.Id==original)).StartAt);Assert.Equal(1,await db.SeriesRevisions.CountAsync(x=>x.SeriesId==id));}
  await Cancel(s,other);var clock=new Clock{UtcNow=Now.AddDays(1)};await using(var db=Db(user,clock)){var service=Series(db,user,clock);var report=await service.ChangeAsync(id,new(Day.AddDays(7),s.Court.Id,new(20,0),1),Guid.NewGuid().ToString(),default);Assert.All(report,x=>Assert.Equal("FREE",x.Result));var moved=await db.Bookings.SingleAsync(x=>x.Id==original);Assert.Equal(Start.AddDays(7).AddHours(1),moved.StartAt);Assert.Equal(600,moved.PaidAmount);Assert.Equal(Start,(await db.Bookings.SingleAsync(x=>x.SeriesId==id&&x.SeriesDate==Day)).StartAt);}
 }
 [Fact]public async Task Prepaid_allocations_are_ordered_idempotent_and_refunds_are_new_negative_records()
 {
  var s=await Seed();var id=await Create(s);var user=new User(s.Owner,s.Org);await using var db=Db(user);var service=Series(db,user);var key=Guid.NewGuid().ToString();var allocations=await service.PrepayAsync(id,new(2,1500,"CASH"),key,default);Assert.Equal(new decimal[]{900,600},allocations.Select(x=>x.Amount));await service.PrepayAsync(id,new(2,1500,"CASH"),key,default);Assert.Equal(2,await db.Payments.CountAsync(x=>x.PrepaymentBlockId==allocations[0].BlockId));await new StaffPaymentsController(db,user,new Clock()).RecordRefund(allocations[0].PaymentId,new(200,PaymentMethod.CASH),Guid.NewGuid().ToString(),default);var detail=await service.DetailAsync(id,default);Assert.Contains(detail.Allocations,x=>x.Amount==-200);Assert.Equal(1300,detail.Allocations.Sum(x=>x.Amount));Assert.Equal(900,(await db.Payments.SingleAsync(x=>x.Id==allocations[0].PaymentId)).Amount);
 }
 [Fact]public async Task Twenty_parallel_series_or_staff_bookings_never_overlap()
 {
  var s=await Seed();var user=new User(s.Owner,s.Org);var outcomes=await Task.WhenAll(Enumerable.Range(0,20).Select(async i=>{await using var db=Db(user);if(i%2==0){var response=await Series(db,user).CreateAsync(new(Request(s),false),Guid.NewGuid().ToString(),default);return response.Id!=null;}try{await Books(db,user).CreateStaffBookingAsync(new(s.Court.Id,s.Customers[1].Phone,"Racer",Start,Start.AddHours(1)),Guid.NewGuid().ToString(),null,default);return true;}catch(ApiException ex){Assert.Equal("SLOT_UNAVAILABLE",ex.Code);return false;}}));Assert.Single(outcomes,x=>x);await using var verify=Db();var rows=await verify.Bookings.Where(x=>x.CourtId==s.Court.Id&&x.Status!=BookingStatus.CANCELLED&&x.Status!=BookingStatus.EXPIRED).OrderBy(x=>x.StartAt).ToListAsync();for(int i=1;i<rows.Count;i++)Assert.True(rows[i-1].EndAt<=rows[i].StartAt);
 }
 [Fact]public async Task FIFO_offers_one_hold_expiry_advances_queue_and_claim_keeps_other_customers_private()
 {
  var s=await Seed();var booked=await Book(s);var first=await Join(s,0);var second=await Join(s,1);Assert.True(first.Position<second.Position);await Cancel(s,booked);var clock=new Clock();var events=new Events();var sender=new Sender();await using(var db=Db(clock:clock)){var queue=new NotificationQueue(db,clock,new EphemeralDataProtectionProvider(),sender);await Wait(db,new SuperAdminUser(),clock,events,queue).DispatchAsync(s.Venue.Id,Day,default);Assert.Equal(1,await db.WaitlistEntries.CountAsync(x=>x.VenueId==s.Venue.Id&&x.Status=="OFFERED"));Assert.Equal("OFFERED",(await db.WaitlistEntries.SingleAsync(x=>x.Id==first.Id)).Status);Assert.Equal(1,await db.Notifications.CountAsync(x=>x.Template=="waitlist_offer"));await Wait(db,new SuperAdminUser(),clock).DispatchAsync(s.Venue.Id,Day,default);Assert.Equal(1,await db.Bookings.CountAsync(x=>x.CourtId==s.Court.Id&&x.Status==BookingStatus.HELD));}
  var other=new User(s.Players[2].Id,null,"Customer");await using(var db=Db(other)){await Assert.ThrowsAsync<ApiException>(()=>Wait(db,other).ClaimAsync(first.Id,default));}
  clock.UtcNow=Now.AddMinutes(16);await using(var db=Db(clock:clock)){await Wait(db,new SuperAdminUser(),clock).DispatchAsync(s.Venue.Id,Day,default);Assert.Equal("EXPIRED",(await db.WaitlistEntries.SingleAsync(x=>x.Id==first.Id)).Status);Assert.Equal("OFFERED",(await db.WaitlistEntries.SingleAsync(x=>x.Id==second.Id)).Status);Assert.Equal(1,await db.Bookings.CountAsync(x=>x.CourtId==s.Court.Id&&x.Status==BookingStatus.HELD));}
  var owner=new User(s.Owner,s.Org);await using(var db=Db(owner,clock)){await Wait(db,owner,clock).ConfirmStaffAsync(second.Id,new(100,"CASH"),Guid.NewGuid().ToString(),default);Assert.Equal("CONVERTED",(await db.WaitlistEntries.SingleAsync(x=>x.Id==second.Id)).Status);}
  var player=new User(s.Players[0].Id,null,"Customer");await using(var db=Db(player,clock)){var json=JsonSerializer.Serialize(await Wait(db,player,clock).ListAsync(null,null,false,default));Assert.DoesNotContain(s.Players[1].Phone!,json);Assert.DoesNotContain(s.Players[1].FullName,json);Assert.DoesNotContain(s.Customers[1].Id.ToString(),json);}
 }
 [Fact]public async Task Three_active_waitlist_entries_are_serialized_and_fourth_is_rejected()
 {
  var s=await Seed();for(var i=0;i<4;i++)await Book(s,Start.AddDays(i));var user=new User(s.Players[0].Id,null,"Customer");var outcomes=await Task.WhenAll(Enumerable.Range(0,4).Select(async i=>{await using var db=Db(user);try{await Wait(db,user).JoinAsync(new(s.Venue.Id,s.Court.Id,Day.AddDays(i),new(19,0),new(20,0),1),Guid.NewGuid().ToString(),false,default);return "OK";}catch(ApiException ex){return ex.Code;}}));Assert.Equal(3,outcomes.Count(x=>x=="OK"));Assert.Contains("TOO_MANY_WAITLIST_ENTRIES",outcomes);
 }
 [Fact]public async Task No_show_approval_blocks_queue_and_two_hold_limit_prevents_an_offer()
 {
  var s=await Seed();var b=await Book(s);await using(var db=Db()){(await db.Customers.SingleAsync(x=>x.Id==s.Customers[0].Id)).NoShowCount=10;await db.SaveChangesAsync();}var first=await Join(s,0);var next=await Join(s,2);await Cancel(s,b);await using(var db=Db()){await Wait(db,new SuperAdminUser()).DispatchAsync(s.Venue.Id,Day,default);Assert.False(await db.WaitlistEntries.AnyAsync(x=>x.VenueId==s.Venue.Id&&x.Status=="OFFERED"));}
  var owner=new User(s.Owner,s.Org);await using(var db=Db(owner)){await Wait(db,owner).ApproveAsync(first.Id,default);}
  var player=new User(s.Players[0].Id,null,"Customer");await using(var db=Db(player)){await Books(db,player).CreateHoldAsync(new(s.Court.Id,Start.AddHours(1),Start.AddHours(2)),Guid.NewGuid().ToString(),null,default);await Books(db,player).CreateHoldAsync(new(s.Court.Id,Start.AddHours(2),Start.AddHours(3)),Guid.NewGuid().ToString(),null,default);}
  await using(var db=Db()){await Wait(db,new SuperAdminUser()).DispatchAsync(s.Venue.Id,Day,default);Assert.Equal("WAITING",(await db.WaitlistEntries.SingleAsync(x=>x.Id==first.Id)).Status);Assert.Equal("WAITING",(await db.WaitlistEntries.SingleAsync(x=>x.Id==next.Id)).Status);}
 }
 [Fact]public async Task Tenant_cannot_read_or_change_another_series_waitlist_or_offer()
 {
  var a=await Seed();var b=await Seed();var series=await Create(b);await Book(b,Start.AddHours(1));var playerB=new User(b.Players[0].Id,null,"Customer");Guid entry;await using(var db=Db(playerB)){entry=(await Wait(db,playerB).JoinAsync(new(b.Venue.Id,b.Court.Id,Day,new(20,0),new(21,0),1),Guid.NewGuid().ToString(),false,default)).Id;}
  var ownerA=new User(a.Owner,a.Org);await using(var db=Db(ownerA)){Assert.Empty(await db.RecurringSeries.ToListAsync());await Assert.ThrowsAsync<ApiException>(()=>Series(db,ownerA).DetailAsync(series,default));await Assert.ThrowsAsync<ApiException>(()=>Series(db,ownerA).StateAsync(series,"pause",new(Day),Guid.NewGuid().ToString(),default));Assert.Empty(await db.WaitlistEntries.ToListAsync());await Assert.ThrowsAsync<ApiException>(()=>Wait(db,ownerA).CancelAsync(entry,true,default));}
 }
 [Fact]public async Task Weekly_summary_replaces_occurrence_reminder_and_is_idempotent()
 {
  var s=await Seed();await Create(s);var clock=new Clock{UtcNow=Start.AddHours(-2)};await using var db=Db(clock:clock);(await db.Venues.SingleAsync(x=>x.Id==s.Venue.Id)).WeeklySeriesSummary=true;await db.SaveChangesAsync();var queue=new NotificationQueue(db,clock,new EphemeralDataProtectionProvider(),new Sender());await queue.QueueEventsAndRemindersAsync(default);await queue.QueueEventsAndRemindersAsync(default);Assert.Equal(1,await db.Notifications.CountAsync(x=>x.VenueId==s.Venue.Id&&x.Template=="weekly_summary"));Assert.Equal(0,await db.Notifications.CountAsync(x=>x.VenueId==s.Venue.Id&&x.Template=="reminder"));
 }
 [Fact]public async Task Parallel_offer_dispatch_creates_exactly_one_normal_hold()
 {
  var seed=await Seed();var original=await Book(seed);await Join(seed,0);await Join(seed,1);await Cancel(seed,original);
  await Task.WhenAll(Enumerable.Range(0,20).Select(async _=>{await using var db=Db();await Wait(db,new SuperAdminUser()).DispatchAsync(seed.Venue.Id,Day,default);}));
  await using var verify=Db();Assert.Equal(1,await verify.Bookings.CountAsync(x=>x.CourtId==seed.Court.Id&&x.Status==BookingStatus.HELD));Assert.Equal(1,await verify.WaitlistEntries.CountAsync(x=>x.VenueId==seed.Venue.Id&&x.Status=="OFFERED"));
 }
 [Fact]public async Task Parallel_online_holds_on_different_courts_share_the_two_hold_limit()
 {
  var seed=await Seed();Court second;await using(var db=Db()){second=new(){OrganizationId=seed.Org,VenueId=seed.Venue.Id,Name="Second",PricePerSlot=1000};db.Courts.Add(second);for(short d=0;d<7;d++)db.CourtHours.Add(new(){CourtId=second.Id,Weekday=d,OpenTime=TimeOnly.MinValue,CloseTime=new(23,59)});await db.SaveChangesAsync();}
  var user=new User(seed.Players[0].Id,null,"Customer");var results=await Task.WhenAll(Enumerable.Range(0,6).Select(async i=>{await using var db=Db(user);var at=SeriesCalendar.Utc(Day.AddDays(1),new(6+i,0),"Asia/Kathmandu");try{await Books(db,user).CreateHoldAsync(new(i%2==0?seed.Court.Id:second.Id,at,at.AddHours(1)),Guid.NewGuid().ToString(),null,default);return "OK";}catch(ApiException ex){return ex.Code;}}));Assert.Equal(2,results.Count(x=>x=="OK"));Assert.Equal(4,results.Count(x=>x=="TOO_MANY_HOLDS"));
 }
 [Fact]public async Task Any_court_waitlist_uses_first_fitting_aligned_slot_after_release()
 {
  var seed=await Seed();Court second;await using(var db=Db()){second=new(){OrganizationId=seed.Org,VenueId=seed.Venue.Id,Name="Second",PricePerSlot=1000};db.Courts.Add(second);for(short d=0;d<7;d++)db.CourtHours.Add(new(){CourtId=second.Id,Weekday=d,OpenTime=TimeOnly.MinValue,CloseTime=new(23,59)});await db.SaveChangesAsync();}
  var owner=new User(seed.Owner,seed.Org);Guid release;await using(var db=Db(owner)){var service=Books(db,owner);await service.CreateStaffBookingAsync(new(seed.Court.Id,seed.Customers[1].Phone,"A",Start,Start.AddHours(2)),Guid.NewGuid().ToString(),null,default);release=(await service.CreateStaffBookingAsync(new(second.Id,seed.Customers[2].Phone,"B",Start,Start.AddHours(2)),Guid.NewGuid().ToString(),null,default)).Id;}
  var player=new User(seed.Players[0].Id,null,"Customer");Guid entry;await using(var db=Db(player)){entry=(await Wait(db,player).JoinAsync(new(seed.Venue.Id,null,Day,new(19,30),new(21,0),1),Guid.NewGuid().ToString(),false,default)).Id;}await Cancel(seed,release);await using var admin=Db();await Wait(admin,new SuperAdminUser()).DispatchAsync(seed.Venue.Id,Day,default);var item=await admin.WaitlistEntries.SingleAsync(x=>x.Id==entry);var booking=await admin.Bookings.SingleAsync(x=>x.Id==item.OfferedBookingId);Assert.Equal(second.Id,booking.CourtId);Assert.Equal(Start.AddHours(1),booking.StartAt);
 }
 [Fact]public async Task Midnight_occurrence_is_stored_on_previous_UTC_day_and_reflects_venue_horizon()
 {
  var seed=await Seed();var owner=new User(seed.Owner,seed.Org);await using var db=Db(owner);var result=await Series(db,owner).CreateAsync(new(Request(seed) with{StartDate=Day.AddDays(7),EndDate=Day.AddDays(7),StartTimeLocal=TimeOnly.MinValue},true),Guid.NewGuid().ToString(),default);var booking=await db.Bookings.SingleAsync(x=>x.SeriesId==result.Id);Assert.Equal(DateTimeOffset.Parse("2026-11-05T18:15:00Z"),booking.StartAt);Assert.Equal(new DateOnly(2026,11,6),booking.SeriesDate);
 }
 [Fact]public async Task Ending_generation_early_keeps_future_cutoff_but_can_fill_weeks_before_it()
 {
  var seed=await Seed();var id=await Create(seed);var owner=new User(seed.Owner,seed.Org);await using(var db=Db(owner)){await Series(db,owner).StateAsync(id,"end",new(Day.AddDays(70)),Guid.NewGuid().ToString(),default);}
  var clock=new Clock{UtcNow=Now.AddDays(14)};await using var system=Db(clock:clock);await Series(system,new SuperAdminUser(),clock).GenerateAsync(id,default);Assert.Equal(10,await system.Bookings.CountAsync(x=>x.SeriesId==id));Assert.DoesNotContain(await system.Bookings.Where(x=>x.SeriesId==id).ToListAsync(),b=>b.SeriesDate>=Day.AddDays(70));
 }
 [Fact]public async Task Generation_does_not_recreate_normally_cancelled_occurrences()
 {
  var seed=await Seed();var id=await Create(seed);Guid booking;await using(var db=Db())booking=await db.Bookings.Where(x=>x.SeriesId==id&&x.SeriesDate==Day.AddDays(7)).Select(x=>x.Id).SingleAsync();await Cancel(seed,booking);await using var admin=Db();await Series(admin,new SuperAdminUser()).GenerateAsync(id,default);Assert.Single(await admin.Bookings.Where(x=>x.SeriesId==id&&x.SeriesDate==Day.AddDays(7)).ToListAsync());Assert.Equal(BookingStatus.CANCELLED,(await admin.Bookings.SingleAsync(x=>x.Id==booking)).Status);
 }

 [Fact]public async Task Past_waitlist_entries_are_removed_without_deleting_booking_or_payment_history()
 {
  var seed=await Seed();await Book(seed);await Join(seed,0);var clock=new Clock{UtcNow=Now.AddDays(1)};await using var admin=Db(clock:clock);await Wait(admin,new SuperAdminUser(),clock).CleanupAsync(default);Assert.False(await admin.WaitlistEntries.AnyAsync(x=>x.VenueId==seed.Venue.Id));Assert.Equal(1,await admin.Bookings.CountAsync(x=>x.VenueId==seed.Venue.Id));
 }

 [Fact]public async Task Cashier_cannot_refund_an_occurrence_through_the_regular_payment_endpoint()
 {
  var seed=await Seed();var id=await Create(seed);var cashier=new User(seed.Owner,seed.Org,"Cashier");await using var db=Db(cashier);var booking=await db.Bookings.FirstAsync(x=>x.SeriesId==id);var error=await Assert.ThrowsAsync<ApiException>(()=>Books(db,cashier).RecordPaymentAsync(booking.Id,new(-100,PaymentMethod.CASH),Guid.NewGuid().ToString(),null,default));Assert.Equal(403,error.StatusCode);Assert.Empty(await db.Payments.ToListAsync());
 }
 [Fact]public async Task Current_price_drop_below_a_prepayment_raises_an_alert_without_losing_money()
 {
  var seed=await Seed();var id=await Create(seed);var user=new User(seed.Owner,seed.Org);await using(var db=Db(user)){await Series(db,user).PrepayAsync(id,new(1,900,"CASH"),Guid.NewGuid().ToString(),default);(await db.Courts.SingleAsync(x=>x.Id==seed.Court.Id)).PricePerSlot=500;await db.SaveChangesAsync();}
  await using var admin=Db();await Series(admin,new SuperAdminUser()).GenerateAsync(id,default);var paid=await admin.Bookings.SingleAsync(x=>x.SeriesId==id&&x.SeriesDate==Day);Assert.Equal(900,paid.TotalAmount);Assert.Equal(900,paid.PaidAmount);Assert.Equal(900,await admin.Payments.Where(x=>x.BookingId==paid.Id).SumAsync(x=>x.Amount));Assert.Contains(await admin.SeriesConflicts.Where(x=>x.SeriesId==id).ToListAsync(),x=>x.Reason=="PAID_EXCEEDS_NEW_PRICE"&&x.ResolvedAt==null);
 }

 [Fact]public async Task Additional_prepaid_blocks_skip_fully_paid_games_and_allocate_remaining_dues_in_order()
 {
  var seed=await Seed();var id=await Create(seed);var user=new User(seed.Owner,seed.Org);await using var db=Db(user);var service=Series(db,user);var first=await service.PrepayAsync(id,new(2,1500,"CASH"),Guid.NewGuid().ToString(),default);var second=await service.PrepayAsync(id,new(2,1200,"CASH"),Guid.NewGuid().ToString(),default);Assert.Equal(first[1].BookingId,second[0].BookingId);Assert.Equal(new decimal[]{300,900},second.Select(x=>x.Amount));Assert.DoesNotContain(second,x=>x.BookingId==first[0].BookingId);
 }
 [Fact]public async Task Free_dates_only_can_save_a_fully_conflicted_plan_without_forcing_a_booking()
 {
  var seed=await Seed();var user=new User(seed.Owner,seed.Org);await using var db=Db(user);var response=await Series(db,user).CreateAsync(new(Request(seed) with{StartTimeLocal=new(23,0),DurationSlots=2},true),Guid.NewGuid().ToString(),default);Assert.NotNull(response.Id);Assert.Empty(await db.Bookings.Where(x=>x.SeriesId==response.Id).ToListAsync());Assert.All(response.Dates,x=>Assert.Equal("CLOSED",x.Result));Assert.Equal(8,await db.SeriesConflicts.CountAsync(x=>x.SeriesId==response.Id));
 }
 [Fact]public async Task Demo_reset_clears_scheduling_rows_and_fake_prepayments_but_keeps_audit_history()
 {
  await using var db=Db();var demoService=new Futsal.Api.Pilot.DemoService(db,new SuperAdminUser(),new Clock());var org=await demoService.CreateAsync(default);var access=await demoService.AccessAsync(org,default);var owner=await db.Users.SingleAsync(x=>x.Email==access.Login);var actor=new User(owner.Id,org);var court=await db.Courts.FirstAsync(x=>x.OrganizationId==org);var customer=await db.Customers.FirstAsync(x=>x.OrganizationId==org);var request=new SeriesWrite(court.Id,customer.Id,5,new(19,0),1,Day,null,"CURRENT");var id=(await Series(db,actor).CreateAsync(new(request,true),Guid.NewGuid().ToString(),default)).Id!.Value;await Series(db,actor).PrepayAsync(id,new(1,500,"CASH"),Guid.NewGuid().ToString(),default);await Wait(db,actor).JoinAsync(new(court.VenueId,court.Id,Day,new(19,0),new(20,0),1,customer.Id),Guid.NewGuid().ToString(),true,default);await demoService.ResetAsync(org,default);Assert.False(await db.RecurringSeries.AnyAsync(x=>x.OrganizationId==org));Assert.False(await db.SeriesPrepayments.AnyAsync(x=>x.OrganizationId==org));Assert.False(await db.WaitlistEntries.AnyAsync(x=>x.OrganizationId==org));Assert.Equal(5,await db.Bookings.CountAsync(x=>x.OrganizationId==org));Assert.Contains(await db.AuditLogs.Where(x=>x.OrganizationId==org).ToListAsync(),x=>x.Action=="DEMO_RESET");
 }

 [Fact]public async Task Verified_phone_can_view_and_cancel_staff_created_recurring_games_without_seeing_others()
 {
  var seed=await Seed();var id=await Create(seed);await using(var db=Db()){(await db.Customers.SingleAsync(x=>x.Id==seed.Customers[0].Id)).UserId=null;await db.SaveChangesAsync();}var player=new User(seed.Players[0].Id,null,"Customer");await using(var db=Db(player)){var service=Books(db,player);var mine=await service.MyBookingsAsync(default);Assert.Equal(8,mine.Count);await service.CancelCustomerAsync(mine[0].Id,"Own game",null,default);Assert.Equal(BookingStatus.CANCELLED,(await db.Bookings.IgnoreQueryFilters().SingleAsync(x=>x.Id==mine[0].Id)).Status);}
  var other=new User(seed.Players[1].Id,null,"Customer");await using var otherDb=Db(other);Assert.Empty(await Books(otherDb,other).MyBookingsAsync(default));
 }

 [Fact]public async Task Reducing_the_horizon_does_not_drop_already_generated_games_during_a_future_change()
 {
  var seed=await Seed();var id=await Create(seed);var user=new User(seed.Owner,seed.Org);await using var db=Db(user);(await db.Venues.SingleAsync(x=>x.Id==seed.Venue.Id)).RecurringHorizonWeeks=2;await db.SaveChangesAsync();var report=await Series(db,user).ChangeAsync(id,new(Day.AddDays(7),seed.Court.Id,new(20,0),1),Guid.NewGuid().ToString(),default);Assert.Equal(7,report.Count);Assert.All(report,x=>Assert.Equal("FREE",x.Result));Assert.Equal(8,await db.Bookings.CountAsync(x=>x.SeriesId==id&&x.Status==BookingStatus.CONFIRMED));Assert.Equal(Start.AddDays(49).AddHours(1),(await db.Bookings.SingleAsync(x=>x.SeriesId==id&&x.SeriesDate==Day.AddDays(49))).StartAt);
 }

 [Fact]public async Task Offer_expiry_racing_normal_holds_has_consistent_lock_order_and_no_overlaps()
 {
  var seed=await Seed();var booked=await Book(seed);await Join(seed,0);await Join(seed,1);await Cancel(seed,booked);await using(var db=Db())await Wait(db,new SuperAdminUser()).DispatchAsync(seed.Venue.Id,Day,default);var clock=new Clock{UtcNow=Now.AddMinutes(16)};var customer=new User(seed.Players[1].Id,null,"Customer");await Task.WhenAll(Enumerable.Range(0,12).Select(async i=>{if(i%2==0){await using var db=Db(clock:clock);await Wait(db,new SuperAdminUser(),clock).DispatchAsync(seed.Venue.Id,Day,default);}else{await using var db=Db(customer,clock);try{await Books(db,customer,clock).CreateHoldAsync(new(seed.Court.Id,Start,Start.AddHours(1)),Guid.NewGuid().ToString(),null,default);}catch(ApiException ex){Assert.Equal("SLOT_UNAVAILABLE",ex.Code);}}}));await using var verify=Db(clock:clock);Assert.Equal(1,await verify.Bookings.CountAsync(x=>x.CourtId==seed.Court.Id&&x.Status==BookingStatus.HELD));
 }

}
