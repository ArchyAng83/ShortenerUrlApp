using System.ComponentModel.DataAnnotations;

namespace ShortenerUrlApp.Shared.DTOs
{
    public record RegisterDto(
        [Required] string UserName,
        [Required][EmailAddress] string Email,
        [Required][StringLength(128, MinimumLength = 10)] string Password);
}
