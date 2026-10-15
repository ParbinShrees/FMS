using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Futsal.Api.Controllers;
using Futsal.Api.Identity;
using Futsal.Application.Abstractions;
using Futsal.Contracts;
using Futsal.Domain.Identity;
using Futsal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace Futsal.Tests;

public sealed class PilotAuthorizationTests(Milestone8PostgresFixture fixture) : IClassFixture<Milestone8PostgresFixture>
{
    [Fact]
    public async Task Pilot_HTTP_endpoints_enforce_roles_and_cross_tenant_screenshot_access()
    {
        await using var factory = new WebApplicationFactory<AuthController>().WithWebHostBuilder(b => b.UseEnvironment("Development").ConfigureServices(services => { services.RemoveAll<DbContextOptions<FutsalDbContext>>(); services.RemoveAll<Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration<FutsalDbContext>>(); services.AddDbContext<FutsalDbContext>(o => o.UseNpgsql(fixture.Container.GetConnectionString(), FutsalDbContext.ConfigurePostgres)); }).ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { { "ConnectionStrings:FutsalPro", fixture.Container.GetConnectionString() }, { "Jwt:SigningKey", Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48)) }, { "Seed:SuperAdminPassword", null }, { "Seed:OwnerPassword", null } })));
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false }); await client.GetAsync("/health/live"); var org = new Organization { Name = "Pilot roles", CreatedAt = DateTimeOffset.UtcNow }; var other = new Organization { Name = "Other", CreatedAt = DateTimeOffset.UtcNow }; var owner = new AppUser { FullName = "Owner" }; var manager = new AppUser { FullName = "Manager" }; var cashier = new AppUser { FullName = "Cashier" }; var customer = new AppUser { FullName = "Player" }; var admin = new AppUser { FullName = "Admin", IsPlatformAdmin = true }; Guid screenshot;
        await using (var db = new FutsalDbContext(fixture.Options, new SuperAdminUser(), new FixedClock(DateTimeOffset.UtcNow))) { db.Organizations.AddRange(org, other); db.Users.AddRange(owner, manager, cashier, customer, admin); await db.SaveChangesAsync(); db.Memberships.AddRange(new Membership { OrganizationId = org.Id, UserId = owner.Id, Role = OrganizationRole.OWNER }, new Membership { OrganizationId = org.Id, UserId = manager.Id, Role = OrganizationRole.MANAGER }, new Membership { OrganizationId = org.Id, UserId = cashier.Id, Role = OrganizationRole.CASHIER }); db.Subscriptions.Add(new() { OrganizationId = org.Id, StartsAt = DateTimeOffset.UtcNow.AddDays(-1), EndsAt = DateTimeOffset.UtcNow.AddDays(30), Status = "ACTIVE" }); var feedback = new Futsal.Domain.Pilot.PilotFeedback { OrganizationId = other.Id, Text = "Private other tenant", Screenshot = [1, 2, 3], CreatedAt = DateTimeOffset.UtcNow }; screenshot = feedback.Id; db.PilotFeedback.Add(feedback); await db.SaveChangesAsync(); }
        using var scope = factory.Services.CreateScope(); var validation = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>>().Get("Bearer").TokenValidationParameters; var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { { "Jwt:SigningKey", System.Text.Encoding.UTF8.GetString(((Microsoft.IdentityModel.Tokens.SymmetricSecurityKey)validation.IssuerSigningKey!).Key) }, { "Jwt:Issuer", validation.ValidIssuer }, { "Jwt:Audience", validation.ValidAudience } }).Build(); var tokens = new JwtTokenService(config, new FixedClock(DateTimeOffset.UtcNow));
        foreach (var item in new[] { (owner, "Owner"), (manager, "Manager"), (cashier, "Cashier"), (customer, "Customer"), (admin, "SuperAdmin") })
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.CreateAccessToken(item.Item1, item.Item2 is "Customer" or "SuperAdmin" ? null : org.Id, item.Item2).AccessToken);
            var onboarding = await client.GetAsync("/api/v1/pilot/onboarding"); Assert.Equal(item.Item2 is "Owner" or "SuperAdmin" ? HttpStatusCode.OK : HttpStatusCode.Forbidden, onboarding.StatusCode);
            var imported = await client.PostAsJsonAsync("/api/v1/pilot/imports/customers", new ImportRequest("name,phone\nPlayer,+9779800000000", true, Guid.NewGuid())); Assert.True(imported.StatusCode == (item.Item2 is "Owner" or "Manager" ? HttpStatusCode.OK : HttpStatusCode.Forbidden), item.Item2 + " import: " + imported.StatusCode + " " + await imported.Content.ReadAsStringAsync());
            var monitoring = await client.GetAsync("/api/v1/pilot/monitoring"); Assert.Equal(item.Item2 == "SuperAdmin" ? HttpStatusCode.OK : HttpStatusCode.Forbidden, monitoring.StatusCode);
            var image = await client.GetAsync($"/api/v1/pilot/feedback/{screenshot}/screenshot"); Assert.Equal(item.Item2 == "SuperAdmin" ? HttpStatusCode.OK : item.Item2 == "Customer" ? HttpStatusCode.Forbidden : HttpStatusCode.NotFound, image.StatusCode);
        }
    }
}
