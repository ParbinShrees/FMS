using Microsoft.AspNetCore.Mvc;
using Futsal.Api.Identity;
using Futsal.Domain.Bookings;
using Microsoft.EntityFrameworkCore;

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
                BookingDomainException domainException => (StatusCodes.Status409Conflict, domainException.Code, domainException.Message),
                DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "CONCURRENCY_CONFLICT", "This record was changed by another request. Reload and try again."),
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
                Title = Futsal.Application.Common.ErrorMessages.Get(code,title),
                Status = status,
                Instance = context.Request.Path,
                Extensions = { ["code"] = code, ["traceId"] = context.TraceIdentifier }
            }, options: null, contentType: "application/problem+json", cancellationToken: context.RequestAborted);
        }
    }
}
