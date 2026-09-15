using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using ShortenerUrlApp.Shared.DTOs;
using ShortenerUrlApp.WebApi.Data;
using ShortenerUrlApp.WebApi.Entities;
using ShortenerUrlApp.WebApi.Services;
using StackExchange.Redis;
using System.ComponentModel.DataAnnotations;

namespace ShortenerUrlApp.Tests
{
    // Phase 4: custom aliases, link expiration (ExpiresAt) and click caps (MaxClicks).
    public class CustomAliasTests
    {
        private static ShortenerUrlDbContext CreateDb() =>
            new(new DbContextOptionsBuilder<ShortenerUrlDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        // Returns the (mocked) IDatabase + service pair; an unconfigured mock behaves as
        // "cache miss / zero pending clicks" because default(RedisValue).HasValue is false.
        private static (ShortenerUrlService Service, Mock<IDatabase> Cache) CreateService(ShortenerUrlDbContext db)
        {
            var cache = new Mock<IDatabase>();

            var redis = new Mock<IConnectionMultiplexer>();
            redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(cache.Object);

            return (new ShortenerUrlService(db, redis.Object), cache);
        }

        private static ShortenerUrl Seed(
            ShortenerUrlDbContext db,
            string shortCode,
            DateTime? expiresAt = null,
            int? maxClicks = null,
            int countOfClick = 0,
            bool isCustomAlias = false)
        {
            var url = new ShortenerUrl
            {
                LongUrl = "https://target.example.com",
                ShortCode = shortCode,
                ExpiresAt = expiresAt,
                MaxClicks = maxClicks,
                CountOfClick = countOfClick,
                IsCustomAlias = isCustomAlias
            };
            db.ShortenerUrls.Add(url);
            db.SaveChanges();
            return url;
        }

        // ---------- ShortenUrlAsync: custom alias ----------

        [Fact]
        public async Task ShortenUrlAsync_ShouldUseCustomAlias_WhenProvided()
        {
            var db = CreateDb();
            var (service, _) = CreateService(db);

            var code = await service.ShortenUrlAsync(
                "https://example.com", "user-1", "my-alias", null, null, CancellationToken.None);

            code.Should().Be("my-alias");

            var saved = await db.ShortenerUrls.SingleAsync(u => u.ShortCode == "my-alias");
            saved.IsCustomAlias.Should().BeTrue();
            saved.ExpiresAt.Should().BeNull();
            saved.MaxClicks.Should().BeNull();
        }

        [Fact]
        public async Task ShortenUrlAsync_ShouldGenerateCode_AndNotFlagCustom_WhenAliasOmitted()
        {
            var db = CreateDb();
            var (service, _) = CreateService(db);

            var code = await service.ShortenUrlAsync(
                "https://example.com", "user-1", null, null, null, CancellationToken.None);

            code.Should().HaveLength(7);
            (await db.ShortenerUrls.SingleAsync(u => u.ShortCode == code))
                .IsCustomAlias.Should().BeFalse();
        }

        [Fact]
        public async Task ShortenUrlAsync_ShouldThrow_WhenAliasAlreadyTaken()
        {
            var db = CreateDb();
            Seed(db, "taken-alias");
            var (service, _) = CreateService(db);

            var act = () => service.ShortenUrlAsync(
                "https://example.com", "user-2", "taken-alias", null, null, CancellationToken.None);

            await act.Should().ThrowAsync<AliasAlreadyInUseException>()
                     .WithMessage("*taken-alias*");
        }

        [Fact]
        public async Task ShortenUrlAsync_ShouldSetExpiresAt_FromMinutes()
        {
            var db = CreateDb();
            var (service, _) = CreateService(db);

            var code = await service.ShortenUrlAsync(
                "https://example.com", "user-1", null, 60, null, CancellationToken.None);

            var saved = await db.ShortenerUrls.SingleAsync(u => u.ShortCode == code);
            saved.ExpiresAt.Should().NotBeNull();
            saved.ExpiresAt.Should()
                 .BeCloseTo(DateTime.UtcNow.AddMinutes(60), TimeSpan.FromMinutes(1));
        }

        [Fact]
        public async Task ShortenUrlAsync_ShouldStoreMaxClicks()
        {
            var db = CreateDb();
            var (service, _) = CreateService(db);

            var code = await service.ShortenUrlAsync(
                "https://example.com", "user-1", "cap5", null, 5, CancellationToken.None);

            (await db.ShortenerUrls.SingleAsync(u => u.ShortCode == code))
                .MaxClicks.Should().Be(5);
        }

        // ---------- CreateShortUrlDto: alias format validation ----------

        [Theory]
        [InlineData("ab")]                   // too short (< 3)
        [InlineData("aaaaaaaaaaaaaaaaaaaaa")] // too long (> 20)
        [InlineData("bad alias")]            // space
        [InlineData("bad*alias")]            // invalid char
        [InlineData("алиас")]                // non-ASCII
        [InlineData("a]]b")]                 // invalid chars
        public void CreateShortUrlDto_ShouldFail_OnInvalidAlias(string alias)
        {
            var dto = new CreateShortUrlDto { LongUrl = "https://example.com", CustomAlias = alias };
            var context = new ValidationContext(dto);
            var results = new List<ValidationResult>();

            Validator.TryValidateObject(dto, context, results, validateAllProperties: true)
                     .Should().BeFalse();
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("my_alias-1")]
        [InlineData("aaaaaaaaaaaaaaaaaaaa")] // exactly 20 chars
        [InlineData("MyAl1as")]              // case-sensitive alphanumerics
        public void CreateShortUrlDto_ShouldPass_OnValidAlias(string alias)
        {
            var dto = new CreateShortUrlDto { LongUrl = "https://example.com", CustomAlias = alias };
            var context = new ValidationContext(dto);
            var results = new List<ValidationResult>();

            Validator.TryValidateObject(dto, context, results, validateAllProperties: true)
                     .Should().BeTrue();
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(525601)] // max is 525600 (one year in minutes)
        public void CreateShortUrlDto_ShouldFail_OnOutOfRangeExpiration(int minutes)
        {
            var dto = new CreateShortUrlDto { LongUrl = "https://example.com", ExpiresInMinutes = minutes };
            var context = new ValidationContext(dto);
            var results = new List<ValidationResult>();

            Validator.TryValidateObject(dto, context, results, validateAllProperties: true)
                     .Should().BeFalse();
        }

        [Fact]
        public void CreateShortUrlDto_ShouldFail_OnNonPositiveMaxClicks()
        {
            var dto = new CreateShortUrlDto { LongUrl = "https://example.com", MaxClicks = 0 };
            var context = new ValidationContext(dto);
            var results = new List<ValidationResult>();

            Validator.TryValidateObject(dto, context, results, validateAllProperties: true)
                     .Should().BeFalse();
        }

        // ---------- Resolve: expiration and click-cap behavior ----------

        [Fact]
        public async Task GetLongUrlAsync_ShouldReturnNull_WhenLinkIsExpired()
        {
            var db = CreateDb();
            Seed(db, "expd001", expiresAt: DateTime.UtcNow.AddMinutes(-1));
            var (service, _) = CreateService(db);

            (await service.GetLongUrlAsync("expd001", CancellationToken.None))
                .Should().BeNull();
        }

        [Fact]
        public async Task GetLongUrlWithStatusAsync_ShouldReportExpired()
        {
            var db = CreateDb();
            Seed(db, "expd002", expiresAt: DateTime.UtcNow.AddSeconds(-30));
            var (service, _) = CreateService(db);

            var result = await service.GetLongUrlWithStatusAsync("expd002");

            result.IsExpired.Should().BeTrue();
            result.LongUrl.Should().BeNull();
        }

        [Fact]
        public async Task GetLongUrlWithStatusAsync_ShouldReportLimitReached_WhenMaxClicksExceeded()
        {
            var db = CreateDb();
            Seed(db, "capd001", maxClicks: 5, countOfClick: 5);
            var (service, _) = CreateService(db);

            var result = await service.GetLongUrlWithStatusAsync("capd001");

            result.IsLimitReached.Should().BeTrue();
            (await service.GetLongUrlAsync("capd001", CancellationToken.None)).Should().BeNull();
        }

        [Fact]
        public async Task GetLongUrlWithStatusAsync_ShouldNotCache_WhenMaxClicksIsSet()
        {
            var db = CreateDb();
            Seed(db, "capd002", maxClicks: 10, countOfClick: 1);
            var (service, cache) = CreateService(db);

            var result = await service.GetLongUrlWithStatusAsync("capd002");

            // Live click counting needs the DB/Redis pending state, so capped links bypass url:* caching.
            result.LongUrl.Should().Be("https://target.example.com");

            // Verify the overload the service actually binds to at runtime (see TTL test note).
            RedisKey urlKey = "url:capd002";
            cache.Verify(c => c.StringSetAsync(
                urlKey, It.IsAny<RedisValue>(), It.IsAny<Expiration>(),
                It.IsAny<ValueCondition>(), It.IsAny<CommandFlags>()), Times.Never);
        }

        [Fact]
        public async Task GetLongUrlWithStatusAsync_ShouldRedirect_WhenNotExpiredAndUnderCap()
        {
            var db = CreateDb();
            Seed(db, "ok00001", expiresAt: DateTime.UtcNow.AddMinutes(10), maxClicks: 5, countOfClick: 1);
            var (service, _) = CreateService(db);

            var result = await service.GetLongUrlWithStatusAsync("ok00001");

            result.Status.Should().Be(RedirectStatus.Ok);
            result.LongUrl.Should().Be("https://target.example.com");
        }

        [Fact]
        public async Task GetLongUrlWithStatusAsync_ShouldReportNotFound_ForUnknownCode()
        {
            var db = CreateDb();
            var (service, _) = CreateService(db);

            (await service.GetLongUrlWithStatusAsync("no-such")).IsNotFound.Should().BeTrue();
        }

        [Fact]
        public async Task GetLongUrlWithStatusAsync_ShouldClampCacheTtl_ToRemainingLifetime()
        {
            var db = CreateDb();
            Seed(db, "ttl0001", expiresAt: DateTime.UtcNow.AddMinutes(30));
            var (service, cache) = CreateService(db);

            object? captured = null;

            // Runtime binding (SE.Redis 2.11): the service's StringSetAsync(key, value, ttlTimeSpan)
            // call resolves to (RedisKey, RedisValue, Expiration, ValueCondition, CommandFlags).
            // Expiration is an opaque wrapper, so the TTL is read via reflection: the observable
            // expiry span on the redirect path is what the test asserts.
            cache.Setup(c => c.StringSetAsync(
                        It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<Expiration>(),
                        It.IsAny<ValueCondition>(), It.IsAny<CommandFlags>()))
                 .Callback((RedisKey _, RedisValue __, Expiration exp, ValueCondition ___, CommandFlags ____) => captured = exp)
                 .ReturnsAsync(true);

            (await service.GetLongUrlWithStatusAsync("ttl0001")).LongUrl
                .Should().Be("https://target.example.com");

            captured.Should().NotBeNull();

            // Expiration is an opaque struct over a packed ulong; its ToString is "PX <milliseconds>".
            var capturedText = captured!.ToString();
            capturedText.Should().StartWith("PX ");
            var usedTtl = TimeSpan.FromMilliseconds(long.Parse(capturedText!.Substring(3)));

            usedTtl.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMinutes(29));
            usedTtl.Should().BeLessThan(TimeSpan.FromMinutes(31));
        }

        // ---------- DeleteExpiredUrlsAsync (worker's unit of work) ----------

        [Fact]
        public async Task DeleteExpiredUrlsAsync_ShouldRemoveExpired_AndClearRedis_KeepActive()
        {
            var db = CreateDb();
            Seed(db, "dead0001", expiresAt: DateTime.UtcNow.AddMinutes(-5));
            Seed(db, "dead0002", expiresAt: DateTime.UtcNow.AddSeconds(-1));
            Seed(db, "live0001", expiresAt: DateTime.UtcNow.AddHours(1));
            Seed(db, "live0002"); // never expires
            var (service, cache) = CreateService(db);

            cache.Setup(c => c.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                 .ReturnsAsync(true);

            var deleted = await service.DeleteExpiredUrlsAsync();

            deleted.Should().Be(2);
            (await db.ShortenerUrls.Select(u => u.ShortCode).ToListAsync())
                .Should().BeEquivalentTo(["live0001", "live0002"]);

            cache.Verify(c => c.KeyDeleteAsync("url:dead0001", CommandFlags.None), Times.Once);
            cache.Verify(c => c.KeyDeleteAsync("clicks:dead0001", CommandFlags.None), Times.Once);
            cache.Verify(c => c.KeyDeleteAsync("url:dead0002", CommandFlags.None), Times.Once);
            cache.Verify(c => c.KeyDeleteAsync("clicks:dead0002", CommandFlags.None), Times.Once);
            cache.Verify(c => c.KeyDeleteAsync(It.Is<RedisKey>(k => k.ToString().Contains("live0001")), It.IsAny<CommandFlags>()), Times.Never);
        }

        [Fact]
        public async Task DeleteExpiredUrlsAsync_ShouldReturnZero_WhenNothingExpired()
        {
            var db = CreateDb();
            Seed(db, "live0003", expiresAt: DateTime.UtcNow.AddDays(1));
            var (service, _) = CreateService(db);

            (await service.DeleteExpiredUrlsAsync()).Should().Be(0);
        }
    }
}
