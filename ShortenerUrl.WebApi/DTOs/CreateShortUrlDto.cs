using System.ComponentModel.DataAnnotations;

namespace ShortenerUrlApp.WebApi.DTOs
{
    public record CreateShortUrlDto([Required][Url] string LongUrl);
}
