using System.ComponentModel.DataAnnotations;

namespace ShortenerUrlApp.WebApi.Entities
{
    public class ShortenerUrl
    {
        [Key]
        public Guid Id { get; set; }
        [Required]
        public string LongUrl { get; set; } = string.Empty;
        [Required]
        public string ShortCode { get; set; } = string.Empty;
        public DateTime CreateAt { get; set; } = DateTime.UtcNow;
        public int CountOfClick { get; set; } = 0;
        public uint RowVersion { get; set; }

        // UTC deadline after which the link stops redirecting (410 Gone). Null = never expires.
        public DateTime? ExpiresAt { get; set; }

        // True when the short code was supplied by the user instead of being generated.
        public bool IsCustomAlias { get; set; }

        // Hard cap on redirect count; once reached the link stops redirecting (410 Gone). Null = unlimited.
        public int? MaxClicks { get; set; }

        // Owner of the link. Null for legacy rows created before authentication existed.
        public string? UserId { get; set; }

        // Navigation property to the owning user.
        public ApplicationUser? User { get; set; }
    }
}
