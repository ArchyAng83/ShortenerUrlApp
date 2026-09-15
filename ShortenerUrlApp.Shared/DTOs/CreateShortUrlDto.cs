using System.ComponentModel.DataAnnotations;
using ShortenerUrlApp.Shared.Validation;

namespace ShortenerUrlApp.Shared.DTOs
{
    // "property:" target is required on positional records: attributes on the bare parameter
    // are not visible to Validator/ModelState and would be silently ignored.
    public record CreateShortUrlDto([property: Required][property: HttpUrl] string LongUrl)
    {
        // Optional user-chosen short code. Null/empty means "generate a random one".
        [StringLength(20, MinimumLength = 3, ErrorMessage = "Alias must be 3-20 characters long")]
        [RegularExpression(@"^[a-zA-Z0-9_-]+$", ErrorMessage = "Only alphanumeric, hyphens and underscores")]
        public string? CustomAlias { get; init; }

        // Link lifetime in minutes from creation; max 1 year. Null = never expires.
        [Range(1, 525600, ErrorMessage = "Expiration must be between 1 minute and 1 year")]
        public int? ExpiresInMinutes { get; init; }

        // Redirect count cap. Null = unlimited.
        [Range(1, int.MaxValue, ErrorMessage = "MaxClicks must be at least 1")]
        public int? MaxClicks { get; init; }
    }
}
