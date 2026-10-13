using Microsoft.AspNetCore.Mvc;

namespace Futsal.Api.Identity;

public static class ProblemResponse
{
    public static Task WriteAsync(HttpContext context, int status, string code, string title, CancellationToken cancellationToken = default)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        return context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Type = "about:blank",
            Title = title,
            Status = status,
            Instance = context.Request.Path,
            Extensions = { ["code"] = code, ["traceId"] = context.TraceIdentifier }
        }, options: null, contentType: "application/problem+json", cancellationToken: cancellationToken);
    }
}
