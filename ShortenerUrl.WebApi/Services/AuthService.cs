using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ShortenerUrlApp.Shared.DTOs;
using ShortenerUrlApp.WebApi.Entities;

namespace ShortenerUrlApp.WebApi.Services
{
    // JWT authentication on top of ASP.NET Core Identity.
    // Login uses UserManager.CheckPasswordAsync (cookie-free), so no SignInManager is required.
    public class AuthService(
        UserManager<ApplicationUser> userManager,
        IConfiguration configuration,
        IEmailSender<ApplicationUser>? emailSender = null) : IAuthService
    {
        public async Task<AuthResultDto> RegisterAsync(RegisterDto dto, CancellationToken ct = default)
        {
            var user = new ApplicationUser
            {
                UserName = dto.UserName,
                Email = dto.Email,
                EmailConfirmed = false
            };

            var result = await userManager.CreateAsync(user, dto.Password);

            if (!result.Succeeded)
            {
                return AuthResultDto.Failure(result.Errors.Select(e => e.Description).ToList());
            }

            if (emailSender is not null && user.Email is not null)
            {
                var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
                var appBaseUrl = configuration["AppBaseUrl"]?.TrimEnd('/') ?? "http://localhost:5209";
                var confirmationLink =
                    $"{appBaseUrl}/confirm-email?email={Uri.EscapeDataString(user.Email)}&token={Uri.EscapeDataString(token)}";

                await emailSender.SendConfirmationLinkAsync(user, user.Email, confirmationLink);
            }

            return AuthResultDto.Success(GenerateToken(user));
        }

        public async Task<AuthResultDto> LoginAsync(LoginDto dto, CancellationToken ct = default)
        {
            var user = await userManager.FindByEmailAsync(dto.Email);

            if (user is null || !await userManager.CheckPasswordAsync(user, dto.Password))
            {
                return AuthResultDto.Failure(["Invalid email or password."]);
            }

            if (!user.EmailConfirmed)
            {
                return AuthResultDto.Failure(["Email not confirmed. Check your inbox for the verification link."]);
            }

            return AuthResultDto.Success(GenerateToken(user));
        }

        public async Task<bool> ConfirmEmailAsync(EmailConfirmationDto dto, CancellationToken ct = default)
        {
            var user = await userManager.FindByEmailAsync(dto.Email);

            if (user is null)
            {
                return false;
            }

            var result = await userManager.ConfirmEmailAsync(user, dto.Token);

            return result.Succeeded;
        }

        private AuthResponseDto GenerateToken(ApplicationUser user)
        {
            var jwtSection = configuration.GetSection("JwtSettings");
            var secret = jwtSection["Secret"]
                ?? throw new InvalidOperationException("JwtSettings:Secret is not configured.");

            var expiryMinutes = int.TryParse(jwtSection["ExpiryMinutes"], out var minutes) ? minutes : 60;
            var nowUtc = DateTime.UtcNow;
            var expiresAtUtc = nowUtc.AddMinutes(expiryMinutes);

            // NameIdentifier covers inbound claim mapping; sub keeps the token standard-compliant.
            var claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = user.Id,
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
                [JwtRegisteredClaimNames.Email] = user.Email ?? string.Empty,
                [ClaimTypes.NameIdentifier] = user.Id,
                [ClaimTypes.Name] = user.UserName ?? string.Empty
            };

            var descriptor = new SecurityTokenDescriptor
            {
                Issuer = jwtSection["Issuer"],
                Audience = jwtSection["Audience"],
                Claims = claims,
                NotBefore = nowUtc,
                Expires = expiresAtUtc,
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                    SecurityAlgorithms.HmacSha256)
            };

            var token = new JsonWebTokenHandler().CreateToken(descriptor);

            return new AuthResponseDto(token, expiresAtUtc, user.UserName ?? string.Empty);
        }
    }
}
