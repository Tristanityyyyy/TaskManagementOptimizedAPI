using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaskManagement.DTOs.Auth;
using TaskManagement.Services;

namespace TaskManagement.Controllers.V1;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController : ApiControllerBase
{
    private readonly IAuthService _auth;

    public AuthController(IAuthService auth)
    {
        _auth = auth;
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth-strict")]
    [ProducesResponseType(typeof(LoginResponseV1), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<LoginResponseV1>> Login(
        [FromBody] LoginRequestV1 dto,
        CancellationToken cancellationToken = default)
        => Ok(await _auth.LoginAsync(dto, cancellationToken));

    [HttpGet("me")]
    [ProducesResponseType(typeof(AuthUserSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthUserSummary>> Me(CancellationToken cancellationToken = default)
        => Ok(await _auth.GetMeAsync(CurrentAccount.Id, cancellationToken));

    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken = default)
    {
        await _auth.LogoutAsync(CurrentAccount.Id, cancellationToken);
        return NoContent();
    }

    [HttpPost("forgot-password")]
    [EnableRateLimiting("auth-strict")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordRequestV1 dto,
        CancellationToken cancellationToken = default)
    {
        await _auth.SendForgotPasswordOtpAsync(dto, cancellationToken);
        return NoContent();
    }

    [HttpPost("verify-otp")]
    [EnableRateLimiting("auth-strict")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> VerifyOtp(
        [FromBody] VerifyOtpRequestV1 dto,
        CancellationToken cancellationToken = default)
    {
        await _auth.VerifyOtpAsync(dto, cancellationToken);
        return NoContent();
    }

    [HttpPost("reset-password")]
    [EnableRateLimiting("auth-strict")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequestV1 dto,
        CancellationToken cancellationToken = default)
    {
        await _auth.ResetPasswordAsync(dto, cancellationToken);
        return NoContent();
    }
}
