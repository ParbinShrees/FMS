using Futsal.Api.Contracts;
using Futsal.Api.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Futsal.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(AuthService authService) : ControllerBase
{
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("staff/login")]
    public async Task<ActionResult<TokenResponse>> StaffLogin(StaffLoginRequest request, CancellationToken cancellationToken)
        => Ok(await authService.StaffLoginAsync(request, HttpContext.Connection.RemoteIpAddress, cancellationToken));

    [AllowAnonymous]
    [EnableRateLimiting("otp-request")]
    [HttpPost("otp/request")]
    public async Task<IActionResult> RequestOtp(OtpRequest request, CancellationToken cancellationToken)
    {
        await authService.RequestOtpAsync(request, cancellationToken);
        return Accepted(new { message = "If the phone number is eligible, an OTP has been sent." });
    }

    [AllowAnonymous]
    [EnableRateLimiting("otp-verify")]
    [HttpPost("otp/verify")]
    public async Task<ActionResult<TokenResponse>> VerifyOtp(OtpVerifyRequest request, CancellationToken cancellationToken)
        => Ok(await authService.VerifyOtpAsync(request, HttpContext.Connection.RemoteIpAddress, cancellationToken));

    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("refresh")]
    public async Task<ActionResult<TokenResponse>> Refresh(RefreshRequest request, CancellationToken cancellationToken)
        => Ok(await authService.RefreshAsync(request, HttpContext.Connection.RemoteIpAddress, cancellationToken));

    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(LogoutRequest request, CancellationToken cancellationToken)
    {
        await authService.LogoutAsync(request, HttpContext.Connection.RemoteIpAddress, cancellationToken);
        return NoContent();
    }
}
