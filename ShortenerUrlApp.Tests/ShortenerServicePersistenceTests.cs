using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using ShortenerUrlApp.WebApi.Data;
using ShortenerUrlApp.WebApi.Entities;
using ShortenerUrlApp.WebApi.Services;
using StackExchange.Redis;

namespace ShortenerUrlApp.Tests
{
    public class ShortenerServicePersistenceTests
    {
        private static ShortenerUrlDbContext CreateDb()
        {
            // The database name is fixed per test instance to avoid the AddDbContext
            // pitfall: its options lambda is re-applied per scope, so a Guid inside the
            // lambda would create a fresh InMemory store for every scope.
            return new ShortenerUrlDbContext(
                new DbContextOptionsBuilder<ShortenerUrlDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString())
                    .Options);
        }

        private static (ShortenerUrlService service, Mock<IDatabase> mockDatabase) CreateService(ShortenerUrlDbContext db)
        {
            var mockRedis = new Mock<IConnectionMultiplexer>();
            var mockDatabase = new Mock<IDatabase>();

            mockDatabase.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                        .ReturnsAsync(true);
            mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(mockDatabase.Object);

            return (new ShortenerUrlService(db, mockRedis.Object, new Mock<ILogger<ShortenerUrlService>>().Object), mockDatabase);
        }

        [Fact]
        public async Task DeleteExpiredUrlsAsync_ShouldRemoveOnlyExpired_AndEvictTheirKeys()
        {
            // Arrange
            using var db = CreateDb();
            db.ShortenerUrls.AddRange(
                new ShortenerUrl { Id = Guid.NewGuid(), LongUrl = "https://a.com", ShortCode = "expired1", ExpiresAt = DateTime.UtcNow.AddMinutes(-1) },
                new ShortenerUrl { Id = Guid.NewGuid(), LongUrl = "https://b.com", ShortCode = "live1", ExpiresAt = DateTime.UtcNow.AddMinutes(1) },
                new ShortenerUrl { Id = Guid.NewGuid(), LongUrl = "https://c.com", ShortCode = "never1" });
            await db.SaveChangesAsync();

            var (service, mockDatabase) = CreateService(db);

            // Act
            var deleted = await service.DeleteExpiredUrlsAsync();

            // Assert
            deleted.Should().Be(1);
            db.ShortenerUrls.Should().HaveCount(2);
            db.ShortenerUrls.Should().NotContain(u => u.ShortCode == "expired1");

            // Redis keys for the deleted link are evicted (url, clicks, click-events); the live link is untouched.
            mockDatabase.Verify(d => d.KeyDeleteAsync((RedisKey)"url:expired1", It.IsAny<CommandFlags>()), Times.Once);
            mockDatabase.Verify(d => d.KeyDeleteAsync((RedisKey)"clicks:expired1", It.IsAny<CommandFlags>()), Times.Once);
            mockDatabase.Verify(d => d.KeyDeleteAsync((RedisKey)"click-events:expired1", It.IsAny<CommandFlags>()), Times.Once);
            mockDatabase.Verify(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()), Times.Exactly(3));
        }

        [Fact]
        public async Task DeleteExpiredUrlsAsync_ShouldReturnZero_WhenNothingExpired()
        {
            // Arrange
            using var db = CreateDb();
            db.ShortenerUrls.AddRange(
                new ShortenerUrl { Id = Guid.NewGuid(), LongUrl = "https://b.com", ShortCode = "live2", ExpiresAt = DateTime.UtcNow.AddMinutes(1) },
                new ShortenerUrl { Id = Guid.NewGuid(), LongUrl = "https://c.com", ShortCode = "never2" });
            await db.SaveChangesAsync();

            var (service, mockDatabase) = CreateService(db);

            // Act
            var deleted = await service.DeleteExpiredUrlsAsync();

            // Assert
            deleted.Should().Be(0);
            mockDatabase.Verify(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()), Times.Never);
        }

        [Fact]
        public async Task GetPendingClicksAsync_ShouldReturnPendingValue_FromRedis()
        {
            // Arrange
            using var db = CreateDb();
            var (service, mockDatabase) = CreateService(db);
            mockDatabase.Setup(d => d.StringGetAsync((RedisKey)"clicks:abc123", It.IsAny<CommandFlags>()))
                        .ReturnsAsync((RedisValue)"7");

            // Act
            var result = await service.GetPendingClicksAsync("abc123");

            // Assert
            result.Should().Be(7);
        }

        [Fact]
        public async Task GetPendingClicksAsync_ShouldReturnZero_WhenNoPendingValue()
        {
            // Arrange
            using var db = CreateDb();
            var (service, mockDatabase) = CreateService(db);
            mockDatabase.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                        .ReturnsAsync(RedisValue.Null);

            // Act
            var result = await service.GetPendingClicksAsync("abc123");

            // Assert
            result.Should().Be(0);
        }
    }
}
