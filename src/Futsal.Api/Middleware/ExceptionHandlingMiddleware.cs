using Microsoft.AspNetCore.Mvc;
using Futsal.Api.Identity;

namespace Futsal.Api.Middleware;

public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            if (context.Response.HasStarted) throw;

            var (status, code, title) = exception switch
            {
                ApiException apiException => (apiException.StatusCode, apiException.Code, apiException.Message),
                UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "FORBIDDEN", "You are not allowed to perform this action."),
                _ => (StatusCodes.Status500InternalServerError, "INTERNAL_ERROR", "An unexpected error occurred.")
            };
            if (status >= 500)
                logger.LogError(exception, "Unhandled request exception. TraceId: {TraceId}", context.TraceIdentifier);

            context.Response.Clear();
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Type = "about:blank",
                Title = title,
                Status = status,
                Instance = context.Request.Path,
                Extensions = { ["code"] = code, ["traceId"] = context.TraceIdentifier }
            }, options: null, contentType: "application/problem+json", cancellationToken: context.RequestAborted);
        }
    }
}
