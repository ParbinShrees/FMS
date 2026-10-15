using Futsal.Infrastructure.Notifications;
using Futsal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace Futsal.Api.Notifications;

public sealed class NotificationJobs(IServiceScopeFactory scopes, ILogger<NotificationJobs> logger, Futsal.Api.Operations.JobHeartbeat? heartbeat = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<FutsalDbContext>();
                // Session-wide PostgreSQL lock serializes workers, including provider requests and cap reservations.
                await db.Database.OpenConnectionAsync(stoppingToken);
                var locked = await db.Database.SqlQueryRaw<bool>("SELECT pg_try_advisory_lock(800008) AS \"Value\"").SingleAsync(stoppingToken);
                if (!locked){heartbeat?.Beat("notifications");continue;}
                try
                {
                    var queue = scope.ServiceProvider.GetRequiredService<NotificationQueue>();
                    var now = scope.ServiceProvider.GetRequiredService<Futsal.Application.Abstractions.IClock>().UtcNow;
                    var schedule = await db.Set<Futsal.Domain.Notifications.NotificationJobRun>().ToDictionaryAsync(x => x.Name, stoppingToken);
                    bool reminderDue = !schedule.TryGetValue("reminders", out var reminderRun) || reminderRun.NextRun <= now;
                    bool dueDue = !schedule.TryGetValue("dues", out var dueRun) || dueRun.NextRun <= now;
                    await queue.QueueEventsAndRemindersAsync(stoppingToken, reminderDue);
                    if (dueDue) await queue.DueRemindersAsync(stoppingToken);
                    foreach (var item in new[] { ("reminders", reminderDue, TimeSpan.FromMinutes(5)), ("dues", dueDue, TimeSpan.FromDays(1)) })
                    {
                        if (!item.Item2) continue;
                        if (!schedule.TryGetValue(item.Item1, out var run)) { run = new() { Name = item.Item1 }; db.Add(run); }
                        run.NextRun = now.Add(item.Item3);
                    }
                    await db.SaveChangesAsync(stoppingToken);
                    await queue.SendQueuedAsync(stoppingToken);
                    heartbeat?.Beat("notifications");
                }
                finally { await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock(800008)", CancellationToken.None); }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning("Notification jobs failed ({Type})", ex.GetType().Name); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
