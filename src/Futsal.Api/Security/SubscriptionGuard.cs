using Futsal.Infrastructure.Persistence;
using Futsal.Api.Identity;
using Futsal.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
namespace Futsal.Api.Security;
public static class SubscriptionGuard
{
 public static async Task RequireWritableAsync(FutsalDbContext db,IClock clock,Guid org,CancellationToken ct)
 {
  var active=await db.Organizations.IgnoreQueryFilters().AnyAsync(x=>x.Id==org&&x.Status=="ACTIVE",ct);
  var license=await db.Subscriptions.IgnoreQueryFilters().SingleOrDefaultAsync(x=>x.OrganizationId==org,ct);
  if(!active||license==null||license.ReadOnly(clock.UtcNow))throw new ApiException(403,"SUBSCRIPTION_INACTIVE","The organization is read-only. Contact the Owner to renew.");
 }
}
