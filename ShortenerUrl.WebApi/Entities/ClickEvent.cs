using System.ComponentModel.DataAnnotations;

namespace ShortenerUrlApp.WebApi.Entities
{
    /// <summary>
    /// A single redirect click on a shortened URL.
    /// Rows are produced asynchronously by ClickEventSyncWorker from Redis list buffers, not inline on the hot path.
    /// </summary>
    public class ClickEvent
    {
        [Key]
        public Guid Id { get; set; }

        public Guid ShortenerUrlId { get; set; }
        public ShortenerUrl ShortenerUrl { get; set; } = null!;

        public DateTime ClickedAt { get; set; } = DateTime.UtcNow;

        // Geo/UA metadata; IP/User-Agent/Referrer are captured at redirect time,
        // Country/City are filled by GeoIP enrichment in ClickEventSyncWorker.
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
        public string? Country { get; set; }
        public string? City { get; set; }
        public string? Referrer { get; set; }
    }
}
