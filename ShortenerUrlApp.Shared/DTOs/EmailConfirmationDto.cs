using System.ComponentModel.DataAnnotations;

namespace ShortenerUrlApp.Shared.DTOs
{
    public record EmailConfirmationDto(
        [Required][EmailAddress] string Email,
        [Required] string Token);
}