using System.Net;
using System.Threading.RateLimiting;
using Futsal.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
namespace Futsal.Tests;
public sealed class TransportSecurityTests
{
 private static async Task<WebApplication> Host()
 {
  var b=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Production"});b.WebHost.UseTestServer();b.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{{"Security:KnownProxies:0","10.0.0.2"}});b.AddTransportHardening();
  b.Services.AddRateLimiter(o=>{o.RejectionStatusCode=429;o.OnRejected=async(c,ct)=>{await Futsal.Api.Identity.ProblemResponse.WriteAsync(c.HttpContext,429,"RATE_LIMITED","Too many requests.",ct);};o.AddPolicy("strict",c=>RateLimitPartition.GetFixedWindowLimiter(c.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions{PermitLimit=1,Window=TimeSpan.FromHours(1),QueueLimit=0}));});
  var app=b.Build();app.UseTransportHardening(false);app.UseRouting();app.UseRateLimiter();app.MapGet("/auth",(HttpContext c)=>Results.Ok(c.Connection.RemoteIpAddress?.ToString())).RequireRateLimiting("strict");await app.StartAsync();return app;
 }
 [Fact]public async Task Trusted_forwarded_addresses_partition_rate_limits_and_untrusted_addresses_cannot_spoof()
 {
  await using var app=await Host();var server=app.GetTestServer();
  async Task<HttpContext> Send(string peer,string client)=>await server.SendAsync(c=>{c.Request.Scheme="https";c.Request.Path="/auth";c.Connection.RemoteIpAddress=IPAddress.Parse(peer);c.Request.Headers["X-Forwarded-For"]=client;c.Request.Headers["X-Forwarded-Proto"]="https";});
  Assert.Equal(200,(await Send("10.0.0.2","203.0.113.1")).Response.StatusCode);var limited=await Send("10.0.0.2","203.0.113.1");Assert.Equal(429,limited.Response.StatusCode);Assert.StartsWith("application/problem+json",limited.Response.ContentType);
  Assert.Equal(200,(await Send("10.0.0.2","203.0.113.2")).Response.StatusCode);Assert.Equal(200,(await Send("10.0.0.3","203.0.113.3")).Response.StatusCode);Assert.Equal(429,(await Send("10.0.0.3","203.0.113.4")).Response.StatusCode);
 }
 [Fact]public async Task Every_response_has_headers_including_errors_and_production_HSTS()
 {
  await using var app=await Host();var response=await app.GetTestServer().SendAsync(c=>{c.Request.Path="/missing";c.Request.Scheme="https";});Assert.Equal(404,response.Response.StatusCode);
  foreach(var header in new[]{"Content-Security-Policy","X-Content-Type-Options","Referrer-Policy","X-Frame-Options","Permissions-Policy","Strict-Transport-Security","X-Correlation-ID"})Assert.True(response.Response.Headers.ContainsKey(header),header);
  Assert.Contains("frame-ancestors 'none'",response.Response.Headers.ContentSecurityPolicy.ToString());
 }
}
