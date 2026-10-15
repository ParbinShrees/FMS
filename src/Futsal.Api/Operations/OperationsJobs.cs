using Futsal.Application.Abstractions;
using Futsal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace Futsal.Api.Operations;
public sealed class JobHeartbeat(IClock clock)
{
 private readonly System.Collections.Concurrent.ConcurrentDictionary<string,DateTimeOffset> beats=new();
 public void Beat(string name)=>beats[name]=clock.UtcNow;
 public bool Healthy=>new[]{"expiry","payments","notifications","maintenance"}.All(name=>beats.TryGetValue(name,out var beat)&&beat>clock.UtcNow.AddMinutes(-10));
}
public sealed class OperationsJobs(IServiceScopeFactory scopes,IConfiguration config,JobHeartbeat heartbeat,ILogger<OperationsJobs> logger):BackgroundService
{
 protected override async Task ExecuteAsync(CancellationToken ct)
 {
  using var timer=new PeriodicTimer(TimeSpan.FromMinutes(1));
  do {
   try{await using var scope=scopes.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FutsalDbContext>();var clock=scope.ServiceProvider.GetRequiredService<IClock>();await RunAsync(db,clock,config,ct);heartbeat.Beat("maintenance");}
   catch(OperationCanceledException)when(ct.IsCancellationRequested){break;}
   catch(Exception ex){logger.LogWarning("Maintenance failed ({Type})",ex.GetType().Name);}
  }while(await timer.WaitForNextTickAsync(ct));
 }
 public static async Task RunAsync(FutsalDbContext db,IClock clock,IConfiguration config,CancellationToken ct)
 {
  var now=clock.UtcNow;
  // Status evaluation is cheap/idempotent, and inline write guards also evaluate dates without waiting for this job.
  var subscriptions=await db.Subscriptions.IgnoreQueryFilters().ToListAsync(ct);foreach(var s in subscriptions)s.Check(now);await db.SaveChangesAsync(ct);
  var otpCutoff=now.AddDays(-Math.Max(1,config.GetValue("Retention:OtpDays",1)));var logCutoff=now.AddDays(-Math.Max(7,config.GetValue("Retention:NotificationDays",90)));var keyCutoff=now.AddDays(-Math.Max(31,config.GetValue("Retention:IdempotencyDays",90)));var holdCutoff=now.AddDays(-Math.Max(31,config.GetValue("Retention:ExpiredHoldDays",90)));
  await db.OtpCodes.Where(x=>x.ExpiresAt<otpCutoff).ExecuteDeleteAsync(ct);
  await db.Notifications.IgnoreQueryFilters().Where(x=>x.Status!="QUEUED"&&x.CreatedAt<logCutoff).ExecuteDeleteAsync(ct);
  await db.IdempotencyKeys.Where(x=>x.CreatedAt<keyCutoff).ExecuteDeleteAsync(ct);
  // Only unpaid abandoned holds may be removed. Paid/cancelled/confirmed bookings and all financial/audit records are retained.
  await db.Bookings.IgnoreQueryFilters().Where(x=>x.Status==Futsal.Domain.Bookings.BookingStatus.EXPIRED&&x.CreatedAt<holdCutoff&&!db.Payments.IgnoreQueryFilters().Any(p=>p.BookingId==x.Id)).ExecuteDeleteAsync(ct);
 }
}
