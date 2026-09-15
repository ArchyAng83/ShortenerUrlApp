using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using ShortenerUrlApp.WebApi.Data;
using ShortenerUrlApp.WebApi.Entities;
using ShortenerUrlApp.WebApi.Services;
using StackExchange.Redis;

namespace ShortenerUrlApp.Tests
{
    public class ShortenerUrlOwnershipTests
    {
        private static ShortenerUrlDbContext CreateDb() =>
            new(new DbContextOptionsBuilder<ShortenerUrlDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        private static ShortenerUrlService CreateService(ShortenerUrlDbContext db)
        {
            var cache = new Mock<IDatabase>();
            cache
                .Setup(c => c.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            var redis = new Mock<IConnectionMultiplexer>();
            redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(cache.Object);

            return new ShortenerUrlService(db, redis.Object);
        }

        private static ShortenerUrl Seed(ShortenerUrlDbContext db, string? userId, string shortCode = "aaaa111")
        {
            var url = new ShortenerUrl
            {
                LongUrl = "https://old.example.com",
                ShortCode = shortCode,
                UserId = userId
            };
            db.ShortenerUrls.Add(url);
            db.SaveChanges();
            return url;
        }

        [Fact]
        public async Task GetAllUrlsAsync_ShouldReturnOnlyCurrentUserUrls()
        {
            // Arrange
            var db = CreateDb();
            Seed(db, "user-1", "own1111");
            Seed(db, "user-2", "oth2222");
            Seed(db, null, "anon333");
            var service = CreateService(db);

            // Act
            var urls = await service.GetAllUrlsAsync("user-1", CancellationToken.None);

            // Assert
            urls.Should().ContainSingle().Which.ShortCode.Should().Be("own1111");
        }

        [Fact]
        public async Task GetAllUrlsAsync_NullUser_ShouldReturnOnlyLegacyUrls()
        {
            var db = CreateDb();
            Seed(db, "user-1");
            Seed(db, null, "legacy1");
            var service = CreateService(db);

            var urls = await service.GetAllUrlsAsync(null, CancellationToken.None);

            urls.Should().ContainSingle().Which.ShortCode.Should().Be("legacy1");
        }

        [Fact]
        public async Task ShortenUrlAsync_ShouldAssignUserId()
        {
            var db = CreateDb();
            var service = CreateService(db);

            var code = await service.ShortenUrlAsync("https://example.com", "user-1", CancellationToken.None);

            code.Should().HaveLength(7);
            var saved = await db.ShortenerUrls.SingleAsync(u => u.ShortCode == code);
            saved.UserId.Should().Be("user-1");
        }

        [Fact]
        public async Task UpdateUrlAsync_ShouldUpdate_WhenOwner()
        {
            var db = CreateDb();
            var url = Seed(db, "owner");
            var service = CreateService(db);

            var updated = await service.UpdateUrlAsync(url.Id, "https://new.example.com", "owner", CancellationToken.None);

            updated.Should().BeTrue();
            (await db.ShortenerUrls.SingleAsync(u => u.Id == url.Id))
                .LongUrl.Should().Be("https://new.example.com");
        }

        [Fact]
        public async Task UpdateUrlAsync_ShouldFail_WhenNotOwner()
        {
            var db = CreateDb();
            var url = Seed(db, "owner");
            var service = CreateService(db);

            var updated = await service.UpdateUrlAsync(url.Id, "https://evil.example.com", "intruder", CancellationToken.None);

            updated.Should().BeFalse();
            (await db.ShortenerUrls.SingleAsync(u => u.Id == url.Id))
                .LongUrl.Should().Be("https://old.example.com");
        }

        [Fact]
        public async Task DeleteUrlAsync_ShouldDelete_WhenOwner()
        {
            var db = CreateDb();
            var url = Seed(db, "owner");
            var service = CreateService(db);

            var deleted = await service.DeleteUrlAsync(url.Id, "owner", CancellationToken.None);

            deleted.Should().BeTrue();
            (await db.ShortenerUrls.AnyAsync(u => u.Id == url.Id)).Should().BeFalse();
        }

        [Fact]
        public async Task DeleteUrlAsync_ShouldFail_WhenNotOwner()
        {
            var db = CreateDb();
            var url = Seed(db, "owner");
            var service = CreateService(db);

            var deleted = await service.DeleteUrlAsync(url.Id, "intruder", CancellationToken.None);

            deleted.Should().BeFalse();
            (await db.ShortenerUrls.AnyAsync(u => u.Id == url.Id)).Should().BeTrue();
        }

        [Fact]
        public async Task DeleteUrlAsync_ShouldClearRedisCache_WhenDeleted()
        {
            var db = CreateDb();
            var url = Seed(db, "owner");

            var cache = new Mock<IDatabase>();
            cache.Setup(c => c.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).ReturnsAsync(true);
            var redis = new Mock<IConnectionMultiplexer>();
            redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(cache.Object);

            var service = new ShortenerUrlService(db, redis.Object);

            await service.DeleteUrlAsync(url.Id, "owner", CancellationToken.None);

            // Both url:* and clicks:* keys must be evicted for the deleted short code.
            cache.Verify(c => c.KeyDeleteAsync($"url:{url.ShortCode}", CommandFlags.None), Times.Once);
            cache.Verify(c => c.KeyDeleteAsync($"clicks:{url.ShortCode}", CommandFlags.None), Times.Once);
        }
    }
}
