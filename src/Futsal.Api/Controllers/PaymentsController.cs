using Microsoft.AspNetCore.RateLimiting;
using Futsal.Api.Contracts;
using Futsal.Api.Identity;
using Futsal.Api.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Futsal.Api.Controllers;

[EnableRateLimiting("payments")]
[ApiController, Route("api/v1/payments")]
public sealed class PaymentsController(PaymentProcessingService paymentService) : ControllerBase
{
    [HttpPost("esewa/initiate"), Authorize]
    public async Task<ActionResult<InitiatePaymentResponse>> InitiateEsewa(
        [FromBody] InitiatePaymentRequest request,
        CancellationToken ct)
    {
        if (request.BookingId == Guid.Empty)
            throw new ApiException(400, "VALIDATION_FAILED", "A valid bookingId is required.");

        var response = await paymentService.InitiateEsewaAsync(request, HttpContext.Connection.RemoteIpAddress, ct);
        return Ok(response);
    }

    [HttpPost("esewa/verify")]
    public async Task<ActionResult<VerifyPaymentResponse>> VerifyEsewa(
        [FromBody] VerifyPaymentRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Data))
            throw new ApiException(400, "VALIDATION_FAILED", "data payload is required.");

        var response = await paymentService.VerifyEsewaAsync(request, HttpContext.Connection.RemoteIpAddress, ct);
        return Ok(response);
    }
}
