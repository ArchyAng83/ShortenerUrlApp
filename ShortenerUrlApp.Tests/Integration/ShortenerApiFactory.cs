using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using ShortenerUrlApp.WebApi.Data;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace ShortenerUrlApp.Tests.Integration
{
    /// <summary>
    /// Boots the real WebApi (WebApplicationFactory) against throwaway Postgres and Redis
    /// containers (Testcontainers), so integration tests exercise the actual HTTP stack,
    /// EF Core migrations, background workers and Redis write-behind path.
    /// </summary>
    public sealed class ShortenerApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
    {
        private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("shortener_test")
            .WithUsername("postgres")
            .WithPassword("postgres_test")
            .Build();

        private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine")
            .Build();

        private bool _disposed;

        public string PostgresConnectionString => _postgres.GetConnectionString();

        /// <summary>Opens a direct scoped DbContext for deterministic test seeding.</summary>
        public ShortenerUrlDbContext CreateDatabase()
        {
            var options = new DbContextOptionsBuilder<ShortenerUrlDbContext>()
                .UseNpgsql(PostgresConnectionString)
                .Options;

            return new ShortenerUrlDbContext(options);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // "Testing" avoids loading appsettings.Development.json; all infra secrets
            // come from the two Testcontainers and the in-memory settings below.
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:DefaultConnection", PostgresConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", _redis.GetConnectionString());
            builder.UseSetting("JwtSettings:Secret", new string('x', 40));
            builder.UseSetting("JwtSettings:Issuer", "ShortenerUrlApp");
            builder.UseSetting("JwtSettings:Audience", "ShortenerUrlApp");
            builder.UseSetting("JwtSettings:ExpiryMinutes", "60");
        }

        async Task IAsyncLifetime.InitializeAsync()
        {
            await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

            // Build the host now (containers are up) so EF migrations apply eagerly
            // and the first test never races the lazy host bootstrap.
            _ = Services;
        }

        Task IAsyncLifetime.DisposeAsync()
        {
            if (_disposed)
            {
                return Task.CompletedTask;
            }

            _disposed = true;
            base.Dispose();

            return Task.WhenAll(
                _postgres.DisposeAsync().AsTask(),
                _redis.DisposeAsync().AsTask());
        }
    }
}
