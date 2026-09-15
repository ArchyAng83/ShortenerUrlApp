using System.ComponentModel.DataAnnotations;

namespace ShortenerUrlApp.Shared.DTOs
{
    public record LoginDto(
        [Required][EmailAddress] string Email,
        [Required] string Password);
}
