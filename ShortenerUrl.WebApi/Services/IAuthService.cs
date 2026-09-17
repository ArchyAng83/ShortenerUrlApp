using ShortenerUrlApp.Shared.DTOs;

namespace ShortenerUrlApp.WebApi.Services
{
    public interface IAuthService
    {
        Task<AuthResultDto> RegisterAsync(RegisterDto dto, CancellationToken ct = default);
        Task<AuthResultDto> LoginAsync(LoginDto dto, CancellationToken ct = default);
        Task<bool> ConfirmEmailAsync(EmailConfirmationDto dto, CancellationToken ct = default);
    }
}
