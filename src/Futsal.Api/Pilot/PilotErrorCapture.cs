using Futsal.Application.Abstractions;
using Futsal.Infrastructure.Persistence;
namespace Futsal.Api.Pilot;

public static class PilotErrorCapture
{
    public static async Task RecordAsync(HttpContext context)
    {
        try { await using var scope = context.RequestServices.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope(); var user = scope.ServiceProvider.GetRequiredService<ICurrentUser>(); if (user.OrganizationId is not Guid org) return; var db = scope.ServiceProvider.GetRequiredService<FutsalDbContext>(); var clock = scope.ServiceProvider.GetRequiredService<IClock>(); db.PilotErrors.Add(new() { OrganizationId = org, CorrelationId = context.TraceIdentifier, CreatedAt = clock.UtcNow }); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)); await db.SaveChangesAsync(timeout.Token); } catch { context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("PilotErrors").LogWarning("Pilot error counter could not be persisted"); }
    }
}
