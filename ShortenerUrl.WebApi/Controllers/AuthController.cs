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
    }
}
