using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using ShortenerUrlApp.Shared.DTOs;
using ShortenerUrlApp.WebApi.Data;
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
            codes.Should().HaveCount(1000); // Нет дубликатов в выборке
            codes.Should().OnlyContain(c => c.Length == 7);
        }

        [Fact]
        public async Task GetLongUrlAsync_ShouldReturnFromCache_IfKeyExists()
        {
            // Arrange: the DB stays empty — a cache hit must not touch it.
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

            // Act
            var result = await service.GetLongUrlAsync("abc123");

            // Assert
            result.Should().Be("https://google.com");
        }

        [Theory]
        [InlineData("not-a-url")]
        [InlineData("ftp://google.com")]
        [InlineData("")]
        public void CreateShortUrlDto_ShouldFail_OnInvalidUrl(string badUrl)
        {
            // Arrange
            // CreateShortUrlDto/UpdateLongUrlDto are classes with validated init properties:
            // positional records break ASP.NET Core MVC complex-object validation
            // ("Record type ... has validation metadata defined on property ...").
            var dto = new CreateShortUrlDto { LongUrl = badUrl };
            var context = new ValidationContext(dto);
            var results = new List<ValidationResult>();

            // Act
            var isValid = Validator.TryValidateObject(dto, context, results, true);

            // Assert
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
