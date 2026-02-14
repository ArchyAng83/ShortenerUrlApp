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
        [MaxLength(7)]
        public string ShortCode { get; set; } = string.Empty;
        public DateTime CreateAt { get; set; } = DateTime.UtcNow;
        public int CountOfClick { get; set; } = 0;
    }
}
