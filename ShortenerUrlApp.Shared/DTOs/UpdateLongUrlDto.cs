using System.ComponentModel.DataAnnotations;

namespace ShortenerUrlApp.Shared.DTOs
{
    public record UpdateLongUrlDto(Guid Id, [Required][Url] string LongUrl);
    
}
