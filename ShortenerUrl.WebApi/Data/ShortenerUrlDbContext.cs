using Microsoft.EntityFrameworkCore;
using ShortenerUrlApp.WebApi.Constants;
using ShortenerUrlApp.WebApi.Entities;

namespace ShortenerUrlApp.WebApi.Data
{
    public class ShortenerUrlDbContext(DbContextOptions<ShortenerUrlDbContext> options) : DbContext(options)
    {
        public DbSet<ShortenerUrl> ShortenerUrls { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ShortenerUrl>(entity =>
            {
                entity.HasIndex(x => x.ShortCode).IsUnique();
                entity.Property(x => x.ShortCode).HasMaxLength(Constant.MAX_LENGTH_SHORT_URL);
            });
                
        }
    }
}
