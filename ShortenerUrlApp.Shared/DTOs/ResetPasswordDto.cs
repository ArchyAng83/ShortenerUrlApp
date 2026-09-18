using System.ComponentModel.DataAnnotations;

namespace ShortenerUrlApp.Shared.DTOs
{
    public record ResetPasswordDto(
        [Required][EmailAddress] string Email,
        [Required] string Token,
        [Required][StringLength(128, MinimumLength = 10)] string NewPassword);
}
