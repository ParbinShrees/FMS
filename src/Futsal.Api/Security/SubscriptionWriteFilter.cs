using Futsal.Application.Abstractions;
using Futsal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Filters;
namespace Futsal.Api.Security;
public sealed class SubscriptionWriteFilter(FutsalDbContext db,ICurrentUser current,IClock clock):IAsyncActionFilter
{
 public async Task OnActionExecutionAsync(ActionExecutingContext context,ActionExecutionDelegate next)
 {
  var path=context.HttpContext.Request.Path.Value??"";
  if(current.OrganizationId is Guid org&&!current.IsSuperAdmin&&context.HttpContext.Request.Method is not ("GET" or "HEAD" or "OPTIONS") && !path.Contains("/auth/")&&!path.Contains("/privacy/")&&!path.Contains("/payments")&&!path.Contains("/release")&&!path.Contains("/cancel"))await SubscriptionGuard.RequireWritableAsync(db,clock,org,context.HttpContext.RequestAborted);
  await next();
 }
}
