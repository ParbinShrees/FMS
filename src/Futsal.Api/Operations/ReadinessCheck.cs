using Futsal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
namespace Futsal.Api.Operations;
public sealed class ReadinessCheck(FutsalDbContext db,JobHeartbeat heartbeat,IConfiguration config):IHealthCheck
{
 public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,CancellationToken ct=default)
 {
  try{
   if(!await db.Database.CanConnectAsync(ct)||(await db.Database.GetPendingMigrationsAsync(ct)).Any()||!heartbeat.Healthy)return HealthCheckResult.Unhealthy();
   var path=Path.GetFullPath(config["DataProtection:KeyPath"]??AppContext.BaseDirectory);var drive=new DriveInfo(Path.GetPathRoot(path)!);
   return drive.AvailableFreeSpace<config.GetValue("Operations:MinimumFreeBytes",100L*1024*1024)?HealthCheckResult.Unhealthy():HealthCheckResult.Healthy();
  }catch{return HealthCheckResult.Unhealthy();}
 }
}
