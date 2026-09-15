using System.ComponentModel.DataAnnotations;
using ShortenerUrlApp.Shared.Validation;

namespace ShortenerUrlApp.Shared.DTOs
{
    // "property:" target is required on positional records: attributes on the bare parameter
    // are not visible to Validator/ModelState and would be silently ignored.
    public record CreateShortUrlDto([property: Required][property: HttpUrl] string LongUrl);
}
