namespace ShortenerUrlApp.Shared.DTOs
{
    public record UrlResposeDto(
        Guid Id,
        string LongUrl,
        string ShortUrl,
        DateTime CreateAt,
        int CountOfClick)
    {
        public DateTime? ExpiresAt { get; init; }
        public bool IsCustomAlias { get; init; }
        public int? MaxClicks { get; init; }
        public bool IsExpired { get; init; }
    }
}
