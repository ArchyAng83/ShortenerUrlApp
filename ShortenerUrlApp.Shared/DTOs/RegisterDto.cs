using System.ComponentModel.DataAnnotations;

namespace ShortenerUrlApp.Shared.DTOs
{
    public record RegisterDto(
        [Required] string UserName,
        [Required][EmailAddress] string Email,
        [Required][MinLength(12)] string Password);
}
