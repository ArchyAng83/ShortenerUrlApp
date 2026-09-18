using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ShortenerUrlApp.Shared.DTOs;
using ShortenerUrlApp.WebApi.Services;

namespace ShortenerUrlApp.WebApi.Controllers
{
    [Route("api/v1/auth")]
    [ApiController]
    public class AuthController(IAuthService authService) : ControllerBase
    {
        [HttpPost("register")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> RegisterAsync([FromBody] RegisterDto dto, CancellationToken ct)
        {
            var result = await authService.RegisterAsync(dto, ct);

            if (!result.Succeeded)
            {
                return BadRequest(result.Errors);
            }

            return Ok(result.Auth);
        }

        [HttpPost("login")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> LoginAsync([FromBody] LoginDto dto, CancellationToken ct)
        {
            var result = await authService.LoginAsync(dto, ct);

            if (!result.Succeeded)
            {
                return Unauthorized(result.Errors);
            }

            return Ok(result.Auth);
        }

        [HttpPost("confirm-email")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> ConfirmEmailAsync([FromBody] EmailConfirmationDto dto, CancellationToken ct)
        {
            var confirmed = await authService.ConfirmEmailAsync(dto, ct);

            return confirmed
                ? Ok()
                : BadRequest(new[] { "Invalid or expired email confirmation token." });
        }

        [HttpPost("forgot-password")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> ForgotPasswordAsync([FromBody] ForgotPasswordDto dto, CancellationToken ct)
        {
            await authService.ForgotPasswordAsync(dto, ct);

            // Always report success so the response does not leak which addresses are registered.
            return Ok();
        }

        [HttpPost("reset-password")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> ResetPasswordAsync([FromBody] ResetPasswordDto dto, CancellationToken ct)
        {
            var reset = await authService.ResetPasswordAsync(dto, ct);

            return reset
                ? Ok()
                : BadRequest(new[] { "Invalid or expired password reset token." });
        }
    }
}
