using System.ComponentModel.DataAnnotations;
using ShortenerUrlApp.Shared.Validation;

namespace ShortenerUrlApp.Shared.DTOs
{
    public record UpdateLongUrlDto(Guid Id, [property: Required][property: HttpUrl] string LongUrl);

}
