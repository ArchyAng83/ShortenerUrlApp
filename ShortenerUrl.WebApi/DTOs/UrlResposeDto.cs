namespace ShortenerUrlApp.WebApi.DTOs
{
    public record UrlResposeDto(
        Guid Id,
        string LongUrl,
        string ShortUrl,
        DateTime CreateAt,
        int CountOfClick);
}
