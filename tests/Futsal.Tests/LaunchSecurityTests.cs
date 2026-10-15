using System.Reflection;
using System.Text;
using Futsal.Api.Controllers;
using Futsal.Api.Security;
using Futsal.Application.Abstractions;
using Futsal.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.DataProtection;
namespace Futsal.Tests;
public sealed class LaunchSecurityTests
{
 private sealed class Sink(TextWriter output):Serilog.Core.ILogEventSink{public void Emit(Serilog.Events.LogEvent message)=>new SafeLogFormatter().Format(message,output);}
 [Fact]public void Every_API_action_is_authorized_or_explicitly_public()
 {
  var allow=new HashSet<string>{"PublicLocationsController.Locations","PublicLocationsController.Search","PublicLocationsController.Venues","PublicLocationsController.SearchVenues","PublicLocationsController.VenueDetails","PaymentsController.VerifyEsewa","PublicLocationsController.Availability","PublicLocationsController.AvailabilityVersion","AuthController.StaffLogin","AuthController.RequestOtp","AuthController.VerifyOtp","AuthController.Refresh","AuthController.Logout"};
  var uncovered=new List<string>();
  foreach(var type in typeof(AuthController).Assembly.GetTypes().Where(t=>t.IsSubclassOf(typeof(ControllerBase))))foreach(var method in type.GetMethods(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly).Where(m=>m.GetCustomAttributes<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>().Any()))
  {
   var authorized=type.GetCustomAttributes<AuthorizeAttribute>().Any()||method.GetCustomAttributes<AuthorizeAttribute>().Any();
   var anonymous=type.GetCustomAttributes<AllowAnonymousAttribute>().Any()||method.GetCustomAttributes<AllowAnonymousAttribute>().Any();
   if((!authorized||anonymous)&&!allow.Contains(type.Name+"."+method.Name))uncovered.Add(type.Name+"."+method.Name);
  }
  Assert.Empty(uncovered);
 }
 [Theory][InlineData("password123",false)][InlineData("1234567890",false)][InlineData("short",false)][InlineData("Correct-Horse-9027!",true)]public void Staff_password_policy(string password,bool valid)=>Assert.Equal(valid,StaffPasswordPolicy.Accepts(password));
 [Fact]public void Production_rejects_development_or_missing_secrets(){var c=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Jwt:SigningKey","development-secret-key-that-is-long"},{"ConnectionStrings:FutsalPro","Host=localhost;Username=postgres;Password=password"}}).Build();Assert.Throws<InvalidOperationException>(()=>ProductionConfiguration.Validate(c,true));ProductionConfiguration.Validate(c,false);}
 [Fact]public void Log_formatter_scrubs_key_flow_personal_data_and_secrets()
 {
  var values=new[]{"+9779800000000","private@example.com","123456","eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.signature"};
  using var output=new StringWriter();using(var logger=new Serilog.LoggerConfiguration().WriteTo.Sink(new Sink(output)).CreateLogger())logger.Information("Auth flow phone {Phone} email {Email} OTP {Otp} token {Token}",values[0],values[1],values[2],values[3]);
  foreach(var value in values)Assert.DoesNotContain(value,output.ToString());
 }
 [Fact]public void Totp_matches_RFC6238_SHA1_vector(){Assert.Equal("287082",OwnerTwoFactor.Code(Encoding.ASCII.GetBytes("12345678901234567890"),1));}
 [Fact]public void Recovery_codes_are_single_use_and_TOTP_cannot_replay()
 {
  var clock=new FixedClock(DateTimeOffset.FromUnixTimeSeconds(59));var factor=new OwnerTwoFactor(new EphemeralDataProtectionProvider(),clock);var user=new AppUser{FullName="Owner",TotpSecret=factor.Protect(OwnerTwoFactor.Base32(Encoding.ASCII.GetBytes("12345678901234567890"))),RecoveryCodeHashes=[OwnerTwoFactor.Hash("RECOVERY-ONE")]};
  Assert.True(factor.Verify(user,"287082"));Assert.False(factor.Verify(user,"287082"));Assert.True(factor.Verify(user,"RECOVERY-ONE"));Assert.False(factor.Verify(user,"RECOVERY-ONE"));
 }
 [Theory][InlineData("TRIAL")][InlineData("ACTIVE")]
 public void Subscription_has_grace_then_readonly_without_deleting_data(string status)
 {
  var now=DateTimeOffset.Parse("2026-10-15T00:00:00Z");var s=new Subscription{Status=status,StartsAt=now.AddDays(-30),EndsAt=now,GraceDays=7};s.Check(now);Assert.Equal("PAST_DUE",s.Status);Assert.False(s.ReadOnly(now.AddDays(6)));Assert.True(s.ReadOnly(now.AddDays(7)));Assert.NotEqual(Guid.Empty,s.Id);
 }
 [Fact]public void Cancelled_subscription_and_future_subscription_are_readonly(){var now=DateTimeOffset.UtcNow;Assert.True(new Subscription{Status="CANCELLED"}.ReadOnly(now));Assert.True(new Subscription{StartsAt=now.AddDays(1)}.ReadOnly(now));}
}
