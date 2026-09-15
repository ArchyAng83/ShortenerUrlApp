using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ShortenerUrlApp.WebApi.Constants;
using ShortenerUrlApp.WebApi.Entities;

namespace ShortenerUrlApp.WebApi.Data
{
    // Inherits IdentityDbContext so all AspNet* tables live in the same PostgreSQL schema.
    public class ShortenerUrlDbContext(DbContextOptions<ShortenerUrlDbContext> options)
        : IdentityDbContext<ApplicationUser>(options)
    {
        public DbSet<ShortenerUrl> ShortenerUrls { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Required: builds the ASP.NET Identity model (AspNetUsers, AspNetRoles, ...).
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<ShortenerUrl>(entity =>
            {
                // Fix the historical "SortenerUrls" typo from the old MySQL migration.
                entity.ToTable("ShortenerUrls");
                entity.HasIndex(x => x.ShortCode).IsUnique();
                entity.Property(x => x.ShortCode).HasMaxLength(Constant.MAX_LENGTH_SHORT_URL);

                // One (optional) user -> many URLs; keep URLs if the user is deleted.
                entity.HasOne(x => x.User)
                      .WithMany()
                      .HasForeignKey(x => x.UserId)
                      .OnDelete(DeleteBehavior.SetNull);
            });
        }
    }
}
