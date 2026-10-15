using Futsal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace Futsal.Infrastructure.Payments;
public static class DemoSafety
{
 public static Task<bool> IsDemoAsync(FutsalDbContext db,Guid org,CancellationToken ct)=>db.Organizations.IgnoreQueryFilters().AnyAsync(x=>x.Id==org&&x.IsDemo,ct);
}
