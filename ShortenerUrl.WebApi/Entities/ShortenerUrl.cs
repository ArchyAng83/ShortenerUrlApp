namespace ShortenerUrlApp.WebApi.Entities
{
    public class ShortenerUrl
    {
        public int Id { get; set; }
        public string? LongUrl { get; set; } = string.Empty;
        public string? ShortUrl { get; set; } = string.Empty;
        public DateTime CreateAt { get; set; } = DateTime.UtcNow;
        public int CountOfClick { get; set; } = 0;
    }
}
