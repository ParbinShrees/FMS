using Futsal.Api.Controllers;
using Futsal.Api.Identity;
using Futsal.Api.Bookings;
using Futsal.Application.Abstractions;
using Futsal.Domain.Identity;
using Futsal.Domain.Locations;
using Futsal.Domain.Bookings;
using Futsal.Domain.Notifications;
using Futsal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
namespace Futsal.Tests;
public sealed class LaunchPrivacyTenancyTests(Milestone8PostgresFixture fixture):IClassFixture<Milestone8PostgresFixture>
{
 private static readonly DateTimeOffset Now=DateTimeOffset.Parse("2026-10-15T00:00:00Z");
 private sealed class User(Guid id,Guid org):ICurrentUser{public Guid? UserId=>id;public Guid? OrganizationId=>org;public string? Role=>"Owner";public bool IsSuperAdmin=>false;public bool IsAuthenticated=>true;}
 private FutsalDbContext Db(ICurrentUser? user=null)=>new(fixture.Options,user??new SuperAdminUser(),new FixedClock(Now));
 private async Task<(Guid Org,Guid Owner,Venue Venue,Court Court,Customer Customer,Booking Booking,object[] Resources)> Seed()
 {
  await using var db=Db();var org=new Organization{Name="Private",CreatedAt=Now};var owner=new AppUser{FullName="Owner",CreatedAt=Now};var city=new Location{Type=LocationType.CITY,NameEn="City",Slug=Guid.NewGuid().ToString(),IsSeeded=true};db.Organizations.Add(org);db.Users.Add(owner);db.Locations.Add(city);await db.SaveChangesAsync();
  var member=new Membership{OrganizationId=org.Id,UserId=owner.Id,Role=OrganizationRole.OWNER};db.Memberships.Add(member);var venue=new Venue{OrganizationId=org.Id,LocationId=city.Id,CityId=city.Id,Name="Venue",Slug=Guid.NewGuid().ToString()};db.Venues.Add(venue);var court=new Court{OrganizationId=org.Id,VenueId=venue.Id,Name="Court",PricePerSlot=1000};db.Courts.Add(court);
  var customer=new Customer{OrganizationId=org.Id,FullName="Private Name",Phone="+9779800000000",Notes="Private notes",CreatedAt=Now};db.Customers.Add(customer);var b=Booking.NewStaffConfirmed();b.OrganizationId=org.Id;b.VenueId=venue.Id;b.CourtId=court.Id;b.CustomerId=customer.Id;b.BookingCode=Guid.NewGuid().ToString();b.StartAt=Now.AddDays(1);b.EndAt=b.StartAt.AddHours(1);b.CreatedAt=Now;b.UpdatedAt=Now;b.TotalAmount=1000;b.PaidAmount=500;b.Notes="Private booking note";db.Bookings.Add(b);
  var payment=new Payment{OrganizationId=org.Id,BookingId=b.Id,Amount=500,Method=PaymentMethod.CASH,Status=PaymentStatus.COMPLETED,CreatedAt=Now,VerifiedAt=Now};db.Payments.Add(payment);
  var n=new Notification{OrganizationId=org.Id,VenueId=venue.Id,BookingId=b.Id,CustomerId=customer.Id,Recipient=customer.Phone,Payload="private encrypted payload",UniqueKey=Guid.NewGuid().ToString(),Template="confirmation",CreatedAt=Now,ScheduledFor=Now};db.Notifications.Add(n);await db.SaveChangesAsync();return(org.Id,owner.Id,venue,court,customer,b,[venue,court,customer,b,payment,n,member]);
 }
 [Fact]public async Task Cross_tenant_matrix_blocks_read_update_and_delete_for_all_tenant_resources()
 {
  var a=await Seed();var b=await Seed();var actor=new User(a.Owner,a.Org);
  foreach(var resource in b.Resources)
  {
   var type=resource.GetType();var id=(Guid)type.GetProperty("Id")!.GetValue(resource)!;
   await using(var db=Db(actor)){Assert.Null(await db.FindAsync(type,id));}
   foreach(var state in new[]{EntityState.Modified,EntityState.Deleted})
   {
    await using var db=Db(actor);var entry=db.Entry(resource);entry.State=state;var ex=await Record.ExceptionAsync(()=>db.SaveChangesAsync());Assert.True(ex is UnauthorizedAccessException or InvalidOperationException,$"{type.Name} {state}: {ex?.GetType().Name}");
   }
  }
  await using var tenant=Db(actor);var privacy=new PrivacyController(tenant,actor,new FixedClock(Now));var denied=await Assert.ThrowsAsync<ApiException>(()=>privacy.Export(b.Customer.Id,default));Assert.Equal(404,denied.StatusCode);await Assert.ThrowsAsync<ApiException>(()=>privacy.Anonymize(b.Customer.Id,default));
  var report=new ReportsController(tenant,actor,new Futsal.Infrastructure.Reports.ReportQueries(tenant),new FixedClock(Now));await Assert.ThrowsAsync<ApiException>(()=>report.Get("daily",b.Venue.Id,new(2026,10,15),new(2026,10,15),null,null,default));
 }
 [Fact]public async Task Privacy_export_and_anonymization_preserve_money_and_are_audited()
 {
  var s=await Seed();var actor=new User(s.Owner,s.Org);await using var db=Db(actor);var api=new PrivacyController(db,actor,new FixedClock(Now));var export=Assert.IsType<OkObjectResult>(await api.Export(s.Customer.Id,default));Assert.Contains("Private Name",JsonSerializer.Serialize(export.Value));await api.Anonymize(s.Customer.Id,default);
  var customer=await db.Customers.SingleAsync(x=>x.Id==s.Customer.Id);Assert.Equal("Anonymized",customer.FullName);Assert.True(customer.OptOut);Assert.Null(customer.Notes);Assert.Single(await db.Payments.Where(x=>x.BookingId==s.Booking.Id).ToListAsync());Assert.Equal(500,(await db.Payments.SingleAsync(x=>x.BookingId==s.Booking.Id)).Amount);Assert.Equal(2,await db.AuditLogs.CountAsync(x=>x.EntityId==s.Customer.Id));Assert.Equal("",(await db.Notifications.SingleAsync(x=>x.CustomerId==s.Customer.Id)).Payload);
 }
 [Fact]public async Task Public_venue_serialized_JSON_contains_no_customer_payment_or_credential_data()
 {
  var s=await Seed();await using var db=Db();var api=new PublicLocationsController(db,new FixedClock(Now),new BookingPricing(db));var result=Assert.IsType<OkObjectResult>(await api.VenueDetails(s.Venue.Slug,default));var json=JsonSerializer.Serialize(result.Value);foreach(var forbidden in new[]{"Private Name","+9779800000000","Private notes","Private booking note","password","EncryptedSecret","RawResponse","CustomerId"})Assert.DoesNotContain(forbidden,json,StringComparison.OrdinalIgnoreCase);
 }
 [Fact]public async Task Refund_retries_are_idempotent_and_original_payment_remains_immutable()
 {
  var s=await Seed();var actor=new User(s.Owner,s.Org);Guid paymentId;
  await using(var db=Db(actor))paymentId=await db.Payments.Where(x=>x.BookingId==s.Booking.Id).Select(x=>x.Id).SingleAsync();
  var request=new Futsal.Api.Contracts.RecordPaymentRequest(200,PaymentMethod.CASH,"refund drill");
  var key=Guid.NewGuid().ToString();
  for(var i=0;i<2;i++){await using var db=Db(actor);await new StaffPaymentsController(db,actor,new FixedClock(Now)).RecordRefund(paymentId,request,key,default);}
  await using(var db=Db(actor))
  {
   Assert.Equal(500,(await db.Payments.SingleAsync(x=>x.Id==paymentId)).Amount);
   Assert.Equal(PaymentStatus.COMPLETED,(await db.Payments.SingleAsync(x=>x.Id==paymentId)).Status);
   Assert.Single(await db.Payments.Where(x=>x.ReversalOf==paymentId).ToListAsync());
   Assert.Equal(300,(await db.Bookings.SingleAsync(x=>x.Id==s.Booking.Id)).PaidAmount);
   await Assert.ThrowsAsync<ApiException>(()=>new StaffPaymentsController(db,actor,new FixedClock(Now)).RecordRefund(paymentId,new(400,PaymentMethod.CASH,null),Guid.NewGuid().ToString(),default));
  }
 }

}
