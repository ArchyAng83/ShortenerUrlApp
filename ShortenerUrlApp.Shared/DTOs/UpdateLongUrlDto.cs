using System.ComponentModel.DataAnnotations;
using ShortenerUrlApp.Shared.Validation;

namespace ShortenerUrlApp.Shared.DTOs
{
    // Class with init properties, same reasoning as CreateShortUrlDto.
    public class UpdateLongUrlDto
    {
        public Guid Id { get; init; }

        [Required]
        [HttpUrl]
        [StringLength(2048, ErrorMessage = "URL must not exceed 2048 characters")]
        public string? LongUrl { get; init; }
    }
}
