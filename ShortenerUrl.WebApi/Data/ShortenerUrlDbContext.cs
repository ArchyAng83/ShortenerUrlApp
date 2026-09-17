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
        public DbSet<ClickEvent> ClickEvents { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Required: builds the ASP.NET Identity model (AspNetUsers, AspNetRoles, ...).
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<ShortenerUrl>(entity =>
            {
                // Fix the historical "SortenerUrls" typo from the old MySQL migration.
                entity.ToTable("ShortenerUrls");
                entity.HasIndex(x => x.ShortCode).IsUnique();
                entity.Property(x => x.RowVersion).IsRowVersion();

                // ShortCode stores generated codes (7 chars) AND custom aliases (up to 20).
                // Generated codes stay at Constant.MAX_LENGTH_SHORT_URL and aliases have a
                // 3-20 char DTO-level limit, so no length-based discrimination is needed here.
                entity.Property(x => x.ShortCode).HasMaxLength(Constant.MAX_LENGTH_CUSTOM_ALIAS);

                // Supports the ExpiredLinksCleanupWorker's periodic sweep.
                entity.HasIndex(x => x.ExpiresAt);

                // One (optional) user -> many URLs; keep URLs if the user is deleted.
                entity.HasOne(x => x.User)
                      .WithMany()
                      .HasForeignKey(x => x.UserId)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<ClickEvent>(entity =>
            {
                entity.ToTable("ClickEvents");

                // One URL -> many click events; dropping a link removes its analytics history.
                entity.HasOne(x => x.ShortenerUrl)
                      .WithMany()
                      .HasForeignKey(x => x.ShortenerUrlId)
                      .OnDelete(DeleteBehavior.Cascade);

                // Every analytics query filters by URL and scans/sorts by ClickedAt.
                entity.HasIndex(x => new { x.ShortenerUrlId, x.ClickedAt });
            });
        }
    }
}
