using System.ComponentModel.DataAnnotations;

namespace ShortenerUrlApp.WebApi.DTOs
{
    public record UpdateLongUrlDto(Guid Id, [Required][Url] string LongUrl);
    
}
