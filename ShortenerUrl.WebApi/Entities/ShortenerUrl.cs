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

        // Owner of the link. Null for legacy rows created before authentication existed.
        public string? UserId { get; set; }

        // Navigation property to the owning user.
        public ApplicationUser? User { get; set; }
    }
}
