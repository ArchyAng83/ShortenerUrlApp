using System.ComponentModel.DataAnnotations;

namespace ShortenerUrlApp.Shared.DTOs
{
    public record ForgotPasswordDto(
        [Required][EmailAddress] string Email);
}