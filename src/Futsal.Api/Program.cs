using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Futsal.Api.Authorization;
using Futsal.Api.Bookings;
using Futsal.Api.Identity;
using Futsal.Api.Locations;
using Futsal.Api.Middleware;
using Futsal.Api.Realtime;
using Futsal.Application.Abstractions;
using Futsal.Domain.Identity;
using Futsal.Infrastructure;
using Futsal.Infrastructure.Payments;
using Futsal.Infrastructure.Persistence;
using Futsal.Api.Payments;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/futsalpro-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14));

var signingKey = builder.Configuration["Jwt:SigningKey"];
if (string.IsNullOrWhiteSpace(signingKey) || Encoding.UTF8.GetByteCount(signingKey) < 32)
    throw new InvalidOperationException("Configure a JWT signing key with at least 32 UTF-8 bytes using user-secrets or Jwt__SigningKey.");

var issuer = builder.Configuration["Jwt:Issuer"] ?? "FutsalPro";
var audience = builder.Configuration["Jwt:Audience"] ?? "FutsalProClients";
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<PasswordHasher<AppUser>>();
builder.Services.AddSingleton<PasswordHasher<OtpCode>>();
builder.Services.AddSingleton<IOtpSender, FakeOtpSender>();
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<DevelopmentSeeder>();
builder.Services.AddScoped<BookingPricing>();
builder.Services.AddScoped<BookingService>();
builder.Services.AddScoped<IBookingNotifier,SignalRBookingNotifier>();
builder.Services.AddHostedService<ExpireHoldsBackgroundService>();
builder.Services.AddDataProtection();
builder.Services.AddSingleton<IVenuePaymentProtector, VenuePaymentProtector>();
builder.Services.Configure<EsewaOptions>(builder.Configuration.GetSection(EsewaOptions.SectionName));
builder.Services.AddHttpClient<EsewaGateway>();
builder.Services.AddScoped<IPaymentGateway, EsewaGateway>();
builder.Services.AddScoped<IPaymentGatewayResolver, PaymentGatewayResolver>();
builder.Services.AddScoped<PaymentProcessingService>();
builder.Services.AddHostedService<PaymentReconciliationBackgroundService>();
builder.Services.AddValidatorsFromAssemblyContaining<CreateHoldRequestValidator>();
var signalR = builder.Services.AddSignalR();
var redisConnection = builder.Configuration["Redis:ConnectionString"];
if (!string.IsNullOrWhiteSpace(redisConnection)) signalR.AddStackExchangeRedis(redisConnection);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = System.Security.Claims.ClaimTypes.NameIdentifier,
            RoleClaimType = System.Security.Claims.ClaimTypes.Role
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs/availability"))
                    context.Token = accessToken;
                return Task.CompletedTask;
            },
            OnChallenge = async context =>
            {
                context.HandleResponse();
                await ProblemResponse.WriteAsync(context.HttpContext, StatusCodes.Status401Unauthorized, "UNAUTHORIZED", "Authentication is required.");
            },
            OnForbidden = context => ProblemResponse.WriteAsync(context.HttpContext, StatusCodes.Status403Forbidden, "FORBIDDEN", "You are not allowed to perform this action.")
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SuperAdmin", policy => policy.RequireAuthenticatedUser().RequireClaim("superadmin", "true"));
    options.AddPolicy("StaffAccess", policy => policy.RequireAuthenticatedUser().Requirements.Add(new OrganizationRoleRequirement(OrganizationRole.OWNER, OrganizationRole.MANAGER, OrganizationRole.CASHIER)));
    options.AddPolicy("CanManageStaff", policy => policy.RequireAuthenticatedUser().Requirements.Add(new OrganizationRoleRequirement(OrganizationRole.OWNER, OrganizationRole.MANAGER)));
    options.AddPolicy("CanViewReports", policy => policy.RequireAuthenticatedUser().Requirements.Add(new OrganizationRoleRequirement(OrganizationRole.OWNER, OrganizationRole.MANAGER)));
    options.AddPolicy("OwnerOnly", policy => policy.RequireAuthenticatedUser().Requirements.Add(new OrganizationRoleRequirement(OrganizationRole.OWNER)));
    options.AddPolicy("CanRefund", policy => policy.RequireAuthenticatedUser().Requirements.Add(new OrganizationRoleRequirement(OrganizationRole.OWNER, OrganizationRole.MANAGER)));
});
var webOrigins = builder.Configuration.GetSection("Cors:WebOrigins").Get<string[]>() ?? ["http://localhost:5054", "https://localhost:7126"];
builder.Services.AddCors(options => options.AddPolicy("web", policy => policy.WithOrigins(webOrigins)
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, OrganizationRoleHandler>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
        await ProblemResponse.WriteAsync(context.HttpContext, StatusCodes.Status429TooManyRequests, "RATE_LIMITED", "Too many requests. Try again later.", cancellationToken);
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    options.AddPolicy("otp-request", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 3, Window = TimeSpan.FromHours(1), QueueLimit = 0, AutoReplenishment = true }));
    options.AddPolicy("otp-verify", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    options.AddPolicy("holds", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
});

builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter())).ConfigureApiBehaviorOptions(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var problem = new ProblemDetails
        {
            Type = "about:blank",
            Title = "The request is invalid.",
            Status = StatusCodes.Status400BadRequest,
            Instance = context.HttpContext.Request.Path,
            Extensions = { ["code"] = "VALIDATION_FAILED" }
        };
        return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
    };
});
builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks().AddCheck("live", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy());

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<FutsalDbContext>();
    await database.Database.MigrateAsync();
    if (app.Environment.IsDevelopment())
    {
        await LocationCatalog.SeedAsync(database, CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>().SeedAsync(CancellationToken.None);
    }
}

app.UseMiddleware<ExceptionHandlingMiddleware>();
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.UseRouting();
app.UseCors("web");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Name == "database" });
app.MapControllers();
app.MapHub<AvailabilityHub>("/hubs/availability");

app.Run();

public partial class Program;
