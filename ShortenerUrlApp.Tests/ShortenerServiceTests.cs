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
    public class ShortenerServiceTests
    {
        [Fact]
        public void GenerateCode_ShouldReturnUniqueNonSequentialCodes()
        {
            // Arrange & Act
            var codes = new HashSet<string>();
            for (int i = 0; i < 1000; i++)
            {
                var code = ShortenerUrlService.GenerateCode();
                codes.Add(code);
            }

            // Assert
            codes.Should().HaveCount(1000);
            codes.Should().OnlyContain(c => c.Length == 7);
        }

        [Fact]
        public async Task GetLongUrlAsync_ShouldReturnFromCache_IfKeyExists()
        {
            using var db = new ShortenerUrlDbContext(
                new DbContextOptionsBuilder<ShortenerUrlDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString())
                    .Options);

            var mockRedis = new Mock<IConnectionMultiplexer>();
            var mockDatabase = new Mock<IDatabase>();

            mockDatabase.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                        .ReturnsAsync("https://google.com");
            mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(mockDatabase.Object);

            var service = new ShortenerUrlService(db, mockRedis.Object);

            var result = await service.GetLongUrlAsync("abc123");

            result.Should().Be("https://google.com");
        }

        [Fact]
        public async Task GetLongUrlAsync_ShouldReturnNull_WhenLinkIsExpired()
        {
            using var db = new ShortenerUrlDbContext(
                new DbContextOptionsBuilder<ShortenerUrlDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString())
                    .Options);

            var mockRedis = new Mock<IConnectionMultiplexer>();
            mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(new Mock<IDatabase>().Object);

            db.ShortenerUrls.Add(new ShortenerUrl
            {
                LongUrl = "https://example.com",
                ShortCode = "expired",
                ExpiresAt = DateTime.UtcNow.AddMinutes(-1)
            });
            await db.SaveChangesAsync();

            var service = new ShortenerUrlService(db, mockRedis.Object);

            var result = await service.GetLongUrlAsync("expired");

            result.Should().BeNull();
        }

        [Theory]
        [InlineData("not-a-url")]
        [InlineData("ftp://google.com")]
        [InlineData("")]
        public void CreateShortUrlDto_ShouldFail_OnInvalidUrl(string badUrl)
        {
            var dto = new CreateShortUrlDto { LongUrl = badUrl };
            var context = new ValidationContext(dto);
            var results = new List<ValidationResult>();

            var isValid = Validator.TryValidateObject(dto, context, results, true);

            isValid.Should().BeFalse();
        }

        [Fact]
        public void CreateShortUrlDto_ShouldPass_OnAbsoluteHttpUrl()
        {
            var dto = new CreateShortUrlDto { LongUrl = "https://google.com" };
            var context = new ValidationContext(dto);
            var results = new List<ValidationResult>();

            var isValid = Validator.TryValidateObject(dto, context, results, true);

            isValid.Should().BeTrue();
        }
    }
}
