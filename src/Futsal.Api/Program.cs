using Futsal.Api.Operations;
using Futsal.Security;
using Futsal.Api.Security;
using Futsal.Api.Notifications;
using Futsal.Infrastructure.Notifications;
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
ProductionConfiguration.Validate(builder.Configuration,builder.Environment.IsProduction());
builder.AddTransportHardening();
ProductionConfiguration.ConfigureKeys(builder);

builder.Host.UseSerilog((context,configuration)=>{
 configuration.ReadFrom.Configuration(context.Configuration).Enrich.FromLogContext().MinimumLevel.Override("Microsoft.AspNetCore",Serilog.Events.LogEventLevel.Warning).WriteTo.Console(new SafeLogFormatter());
 if(context.HostingEnvironment.IsDevelopment())configuration.WriteTo.File(new SafeLogFormatter(),"logs/futsalpro-.log",rollingInterval:RollingInterval.Day,retainedFileCountLimit:14);
});

var signingKey = builder.Configuration["Jwt:SigningKey"];
if (string.IsNullOrWhiteSpace(signingKey) || Encoding.UTF8.GetByteCount(signingKey) < 32)
    throw new InvalidOperationException("Configure a JWT signing key with at least 32 UTF-8 bytes using user-secrets or Jwt__SigningKey.");

var issuer = builder.Configuration["Jwt:Issuer"] ?? "FutsalPro";
var audience = builder.Configuration["Jwt:Audience"] ?? "FutsalProClients";
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<JobHeartbeat>();
builder.Services.AddHostedService<OperationsJobs>();
builder.Services.Configure<HostOptions>(o=>o.ShutdownTimeout=TimeSpan.FromSeconds(45));
builder.Services.AddSingleton<PasswordHasher<AppUser>>();
builder.Services.AddSingleton<PasswordHasher<OtpCode>>();
builder.Services.AddScoped<IOtpSender, QueuedOtpSender>();
builder.Services.AddScoped<NotificationQueue>();
builder.Services.AddScoped<NotificationEvents>();
builder.Services.AddScoped<Futsal.Infrastructure.Reports.ReportQueries>();
QuestPDF.Settings.License = Enum.Parse<QuestPDF.Infrastructure.LicenseType>(builder.Configuration["Reports:PdfLicense"] ?? "Community");
builder.Services.AddHttpClient<HttpSmsSender>(client => client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddScoped<ConsoleSender>();
builder.Services.AddScoped<INotificationSender>(sp => (builder.Configuration["Sms:Sender"] ?? (builder.Environment.IsDevelopment() ? "Console" : "Http")) == "Console" ? sp.GetRequiredService<ConsoleSender>() : sp.GetRequiredService<HttpSmsSender>());
builder.Services.AddHostedService<NotificationJobs>();
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddScoped<Futsal.Api.Pilot.OnboardingService>();
builder.Services.AddScoped<Futsal.Api.Pilot.PilotImportService>();
builder.Services.AddScoped<Futsal.Api.Pilot.FeedbackService>();
builder.Services.AddScoped<Futsal.Api.Pilot.PilotMonitoring>();
builder.Services.AddScoped<Futsal.Api.Pilot.DemoService>();
builder.Services.AddHostedService<Futsal.Api.Pilot.PilotWatchdogJob>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<OwnerTwoFactor>();
builder.Services.AddScoped<DevelopmentSeeder>();
builder.Services.AddScoped<BookingPricing>();
builder.Services.AddScoped<BookingService>();
builder.Services.AddScoped<IBookingNotifier, SignalRBookingNotifier>();
builder.Services.AddHostedService<ExpireHoldsBackgroundService>();

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
    options.GlobalLimiter=PartitionedRateLimiter.Create<HttpContext,string>(context=>RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions{PermitLimit=600,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));
    options.AddPolicy("payments",context=>RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions{PermitLimit=30,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));
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
        _ => new FixedWindowRateLimiterOptions { PermitLimit = Math.Clamp(builder.Configuration.GetValue("RateLimits:HoldsPerMinute",20),1,200), Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
});

builder.Services.AddControllers(options=>options.Filters.Add<SubscriptionWriteFilter>()).AddJsonOptions(options =>
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
builder.Services.AddHealthChecks().AddCheck<ReadinessCheck>("readiness").AddCheck("live", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy());

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<FutsalDbContext>();
    for(var attempt=0;;attempt++)
    {
        try{await database.Database.OpenConnectionAsync();await database.Database.CloseConnectionAsync();break;}
        catch(Exception) when(attempt<5){await Task.Delay(TimeSpan.FromSeconds(Math.Min(8,Math.Pow(2,attempt))));}
    }
    if(app.Environment.IsDevelopment()||args.Contains("--migrate"))await database.Database.MigrateAsync();
    else if((await database.Database.GetPendingMigrationsAsync()).Any())throw new InvalidOperationException("Apply migrations with the migration role before starting the API.");
    if(args.Contains("--migrate"))return;
    if (app.Environment.IsDevelopment())
    {
        await LocationCatalog.SeedAsync(database, CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>().SeedAsync(CancellationToken.None);
    }
}

app.UseTransportHardening(false);
app.UseMiddleware<ExceptionHandlingMiddleware>();
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.UseRequestLocalization(new RequestLocalizationOptions().SetDefaultCulture("en").AddSupportedCultures("en", "ne").AddSupportedUICultures("en", "ne"));
app.UseRouting();
app.UseCors("web");
app.UseRequestTimeouts();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Name == "readiness" });
app.MapControllers().WithRequestTimeout("api");
app.MapHub<AvailabilityHub>("/hubs/availability");

app.Run();

public partial class Program;
