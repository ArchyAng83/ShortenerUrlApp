using System.ComponentModel.DataAnnotations;
using ShortenerUrlApp.Shared.Validation;

namespace ShortenerUrlApp.Shared.DTOs
{
    // Plain class (not a positional record) on purpose: ASP.NET Core MVC throws
    // "Record type ... has validation metadata defined on property ... that will be ignored"
    // during complex-object validation when a positional record carries attributes via
    // [property:], and bare positional parameters are invisible to Validator.TryValidateObject
    // (used by unit tests). Init-only validated properties satisfy both consumers.
    public class CreateShortUrlDto
    {
        [Required]
        [HttpUrl]
        [StringLength(2048, ErrorMessage = "URL must not exceed 2048 characters")]
        public string? LongUrl { get; init; }

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
