using FluentValidation;
using Futsal.Api.Bookings;
using Futsal.Api.Contracts;
using Futsal.Api.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Futsal.Api.Controllers;

[ApiController,Route("api/v1/bookings"),Authorize]
public sealed class BookingsController(BookingService bookings,IValidator<CreateHoldRequest> holdValidator,IValidator<CancelBookingRequest> cancelValidator):ControllerBase
{
    [HttpPost("holds")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("holds")]
    public async Task<ActionResult<BookingResponse>> CreateHold(CreateHoldRequest request,[FromHeader(Name="Idempotency-Key")]string? key,CancellationToken ct)
    {
        await ValidateAsync(holdValidator,request,ct);RequireKey(key);var result=await bookings.CreateHoldAsync(request,key!,HttpContext.Connection.RemoteIpAddress,ct);return StatusCode(StatusCodes.Status201Created,result);
    }
    [HttpPost("{id:guid}/release")]
    public async Task<IActionResult> Release(Guid id,CancellationToken ct){await bookings.ReleaseHoldAsync(id,HttpContext.Connection.RemoteIpAddress,ct);return NoContent();}
    [HttpGet("mine")]
    public async Task<IActionResult> Mine(CancellationToken ct)=>Ok(await bookings.MyBookingsAsync(ct));
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id,CancelBookingRequest request,CancellationToken ct){await ValidateAsync(cancelValidator,request,ct);await bookings.CancelCustomerAsync(id,request.Reason,HttpContext.Connection.RemoteIpAddress,ct);return NoContent();}

    internal static async Task ValidateAsync<T>(IValidator<T> validator,T request,CancellationToken ct)
    {
        var result=await validator.ValidateAsync(request,ct);if(!result.IsValid)throw new ApiException(400,"VALIDATION_FAILED",string.Join(" ",result.Errors.Select(x=>x.ErrorMessage)));
    }
    internal static void RequireKey(string? key){if(string.IsNullOrWhiteSpace(key)||key.Length>200)throw new ApiException(400,"VALIDATION_FAILED","A valid Idempotency-Key header is required.");}
}
