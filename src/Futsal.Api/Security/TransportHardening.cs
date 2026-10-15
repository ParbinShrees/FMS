using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Timeouts;
#if FUTSAL_WEB
namespace Futsal.Web.Security;
#else
namespace Futsal.Security;
#endif
public static class TransportHardening
{
 public static void AddTransportHardening(this WebApplicationBuilder builder)
 {
  builder.WebHost.ConfigureKestrel(o=>{o.AddServerHeader=false;o.Limits.MaxRequestBodySize=65536;o.Limits.RequestHeadersTimeout=TimeSpan.FromSeconds(15);o.Limits.KeepAliveTimeout=TimeSpan.FromMinutes(2);});
  builder.Services.Configure<ForwardedHeadersOptions>(o=>{
   o.ForwardedHeaders=ForwardedHeaders.XForwardedFor|ForwardedHeaders.XForwardedProto;o.ForwardLimit=1;o.RequireHeaderSymmetry=true;
   o.KnownIPNetworks.Clear();o.KnownProxies.Clear();
   foreach(var value in builder.Configuration.GetSection("Security:KnownProxies").Get<string[]>()??[]){if(!IPAddress.TryParse(value,out var ip))throw new InvalidOperationException("Invalid known proxy address.");o.KnownProxies.Add(ip);}
   // No configured proxy means forwarded headers are disabled, never trusted from arbitrary peers.
   if(o.KnownProxies.Count==0)o.ForwardedHeaders=ForwardedHeaders.None;
  });
  builder.Services.AddHsts(o=>{o.MaxAge=TimeSpan.FromDays(365);o.IncludeSubDomains=true;});
  builder.Services.AddHttpsRedirection(o=>o.HttpsPort=443);
  builder.Services.AddRequestTimeouts(o=>o.AddPolicy("api",new RequestTimeoutPolicy{Timeout=TimeSpan.FromSeconds(30),TimeoutStatusCode=504,WriteTimeoutResponse=async c=>{c.Response.ContentType="application/problem+json";await c.Response.WriteAsJsonAsync(new{status=504,title="Request timed out.",code="REQUEST_TIMEOUT"},options:null,contentType:"application/problem+json");}}));
  builder.Services.AddAntiforgery(o=>{o.Cookie.Name="__Host-futsalpro-antiforgery";o.Cookie.HttpOnly=true;o.Cookie.SecurePolicy=CookieSecurePolicy.Always;o.Cookie.SameSite=SameSiteMode.Strict;o.HeaderName="X-CSRF-TOKEN";});
 }
 public static void UseTransportHardening(this WebApplication app,bool web)
 {
  app.UseForwardedHeaders();
  app.Use(async(context,next)=>{
   var correlation=Guid.NewGuid().ToString("N");context.TraceIdentifier=correlation;
   var nonce=Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));context.Items["CspNonce"]=nonce;
   using var scope=context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Request").BeginScope(new Dictionary<string,object>{{"CorrelationId",correlation}});
   context.Response.OnStarting(()=>{
    var h=context.Response.Headers;h["X-Correlation-ID"]=correlation;h["X-Content-Type-Options"]="nosniff";h["Referrer-Policy"]="strict-origin-when-cross-origin";h["X-Frame-Options"]="DENY";
    h["Permissions-Policy"]=web&&context.Request.Path.StartsWithSegments("/v")?"geolocation=(self), camera=(), microphone=()":"geolocation=(), camera=(), microphone=()";
    // MudBlazor positions components using inline style attributes; scripts never allow unsafe-inline or unsafe-eval.
    h["Content-Security-Policy"]=web?$"default-src 'self'; script-src 'self' 'nonce-{nonce}'; style-src 'self' 'unsafe-inline'; font-src 'self' data:; img-src 'self' data: blob:; connect-src 'self'; worker-src 'self'; manifest-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self' https://epay.esewa.com.np https://rc-epay.esewa.com.np":"default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
    if(!app.Environment.IsDevelopment()&&context.Request.IsHttps)h["Strict-Transport-Security"]="max-age=31536000; includeSubDomains";
    if(context.Request.Path.StartsWithSegments("/api")||context.Request.Path.StartsWithSegments("/bff"))h.CacheControl="no-store";
    return Task.CompletedTask;
   });
   await next();
  });
  if(!app.Environment.IsDevelopment())app.UseHsts();
  app.UseHttpsRedirection();
 }
}
