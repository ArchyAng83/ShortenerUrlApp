using System.ComponentModel.DataAnnotations;

namespace ShortenerUrlApp.Shared.DTOs
{
    public record CreateShortUrlDto([Required][Url] string LongUrl);
}
