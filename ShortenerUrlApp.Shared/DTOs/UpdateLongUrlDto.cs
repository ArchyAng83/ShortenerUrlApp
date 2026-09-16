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
        public string? LongUrl { get; init; }
    }
}
