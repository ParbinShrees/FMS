using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;
using Futsal.Infrastructure.Persistence;
using Futsal.Api.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
namespace Futsal.Tests;
public sealed class EndpointInventoryTests(Milestone8PostgresFixture fixture):IClassFixture<Milestone8PostgresFixture>
{
 [Fact]public async Task Actual_endpoint_data_source_has_no_unreviewed_anonymous_routes()
 {
  await using var factory=new WebApplicationFactory<AuthController>().WithWebHostBuilder(b=>b.UseEnvironment("Development").ConfigureServices(services=>{services.RemoveAll<DbContextOptions<FutsalDbContext>>();services.RemoveAll<Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration<FutsalDbContext>>();services.AddDbContext<FutsalDbContext>(o=>o.UseNpgsql(fixture.Container.GetConnectionString(),FutsalDbContext.ConfigurePostgres));}).ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:FutsalPro",fixture.Container.GetConnectionString()},{"Jwt:SigningKey",Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48))},{"Seed:SuperAdminPassword",null},{"Seed:OwnerPassword",null}})));
  using var client=factory.CreateClient(new(){BaseAddress=new Uri("https://localhost"),AllowAutoRedirect=false});await client.GetAsync("/health/live");
  var publicRoutes=new HashSet<string>{"/health/live","/health/ready","/hubs/availability","/hubs/availability/negotiate","/openapi/{documentName}.json","api/v1/auth/staff/login","api/v1/auth/otp/request","api/v1/auth/otp/verify","api/v1/auth/refresh","api/v1/auth/logout","api/v1/auth/invites/accept","api/v1/payments/esewa/verify","api/v1/public/locations","api/v1/public/locations/search","api/v1/public/locations/{slug}/venues","api/v1/public/venues","api/v1/public/venues/{slug}","api/v1/public/venues/{slug}/availability","api/v1/public/venues/{slug}/availability/version"};
  var endpoints=factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToArray();Assert.NotEmpty(endpoints);
  foreach(var endpoint in endpoints){var auth=endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();if(auth.Count==0||endpoint.Metadata.GetMetadata<IAllowAnonymous>()!=null)Assert.Contains(endpoint.RoutePattern.RawText!,publicRoutes);}
 }
}
