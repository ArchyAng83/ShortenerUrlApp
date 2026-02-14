using Microsoft.EntityFrameworkCore;
using ShortenerUrlApp.WebApi.Entities;

namespace ShortenerUrlApp.WebApi.Data
{
    public class ShortenerUrlDbContext(DbContextOptions<ShortenerUrlDbContext> options) : DbContext(options)
    {
        DbSet<ShortenerUrl> SortenerUrls { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ShortenerUrl>(entity =>
            {
                entity.HasIndex(x => x.ShortCode).IsUnique();
            });
                
        }
    }
}
