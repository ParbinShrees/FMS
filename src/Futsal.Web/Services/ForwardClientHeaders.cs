namespace Futsal.Web.Services;
public sealed class ForwardClientHeaders(IHttpContextAccessor context):DelegatingHandler
{
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
 {
  request.Headers.TryAddWithoutValidation("X-Forwarded-For",context.HttpContext?.Connection.RemoteIpAddress?.ToString()??"127.0.0.1");
  request.Headers.TryAddWithoutValidation("X-Forwarded-Proto","https");
  return base.SendAsync(request,ct);
 }
}
