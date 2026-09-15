using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using ShortenerUrlApp.WebApi.Controllers;
using ShortenerUrlApp.WebApi.Data;
using ShortenerUrlApp.WebApi.Entities;
using ShortenerUrlApp.WebApi.Services;
using StackExchange.Redis;
using System.Security.Claims;

namespace ShortenerUrlApp.Tests
{
    public class QRCodeServiceTests
    {
        // Every PNG file starts with this 8-byte signature.
        private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        private static (QRCodeService service, Mock<IDatabase> cache) CreateService()
        {
            var cache = new Mock<IDatabase>();

            var redis = new Mock<IConnectionMultiplexer>();
            redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(cache.Object);

            return (new QRCodeService(redis.Object), cache);
        }

        // Setups target the exact six-argument overload QRCodeService calls;
        // SE.Redis 2.11 has several same-shaped StringSetAsync signatures and shorter
        // Setups bind to a different one (this broke CustomAliasTests before).
        private static void SetupSet(Mock<IDatabase> cache) =>
            cache.Setup(c => c.StringSetAsync(
                    It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                    It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

        [Fact]
        public async Task GenerateQRCodeAsync_ShouldReturnPng_WhenCacheMiss()
        {
            var (service, cache) = CreateService();
            cache.Setup(c => c.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                 .ReturnsAsync(RedisValue.Null);
            SetupSet(cache);

            var png = await service.GenerateQRCodeAsync("https://example.com/abc123");

            png.Should().HaveCountGreaterThan(8);
            png[..8].Should().Equal(PngSignature);
        }

        [Fact]
        public async Task GenerateQRCodeAsync_ShouldCachePngForOneDay_WhenCacheMiss()
        {
            var (service, cache) = CreateService();
            cache.Setup(c => c.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                 .ReturnsAsync(RedisValue.Null);
            SetupSet(cache);

            await service.GenerateQRCodeAsync("https://example.com/abc123");

            // The read must use the documented "qrcode:{hash}" key shape.
            cache.Verify(c => c.StringGetAsync(
                It.Is<RedisKey>(k => k.ToString().StartsWith("qrcode:")),
                It.IsAny<CommandFlags>()), Times.Once);
            cache.Verify(c => c.StringSetAsync(
                It.Is<RedisKey>(k => k.ToString().StartsWith("qrcode:")),
                It.IsAny<RedisValue>(), TimeSpan.FromDays(1),
                false, When.Always, CommandFlags.None), Times.Once);
        }

        [Fact]
        public async Task GenerateQRCodeAsync_ShouldReturnCachedBytes_AndNotRender_WhenCacheHit()
        {
            var (service, cache) = CreateService();
            byte[] cachedPng = [1, 2, 3, 4];
            cache.Setup(c => c.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                 .ReturnsAsync(cachedPng);

            var png = await service.GenerateQRCodeAsync("https://example.com/abc123");

            png.Should().Equal(cachedPng);
            cache.Verify(c => c.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()), Times.Never);
        }

        [Fact]
        public async Task GenerateQRCodeAsync_SecondCall_ShouldBeServedFromRedis()
        {
            var (service, cache) = CreateService();

            // Shared state emulating Redis: StringGet returns what StringSet stored.
            RedisValue stored = RedisValue.Null;
            cache.Setup(c => c.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                 .ReturnsAsync(() => stored);
            cache.Setup(c => c.StringSetAsync(
                    It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                    It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
                .Callback((RedisKey k, RedisValue v, TimeSpan? e, bool keepTtl, When w, CommandFlags f) => stored = v)
                .ReturnsAsync(true);

            var first = await service.GenerateQRCodeAsync("https://example.com/abc123");
            var second = await service.GenerateQRCodeAsync("https://example.com/abc123");

            first.Should().NotBeEmpty();
            second.Should().Equal(first);
            // The bitmap is rendered once; the repeat call is a pure cache read.
            cache.Verify(c => c.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()), Times.Once);
            cache.Verify(c => c.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()), Times.Exactly(2));
        }
    }

    public class QRCodeControllerTests
    {
        private static ShortenerUrlDbContext CreateDb() =>
            new(new DbContextOptionsBuilder<ShortenerUrlDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        private static ShortenerUrl Seed(ShortenerUrlDbContext db, string? userId, string shortCode = "qr77aaa")
        {
            var url = new ShortenerUrl
            {
                LongUrl = "https://destination.example.com",
                ShortCode = shortCode,
                UserId = userId
            };
            db.ShortenerUrls.Add(url);
            db.SaveChanges();
            return url;
        }

        private static QRCodeController CreateController(IQRCodeService qrCodeService, ShortenerUrlDbContext db, string userId)
        {
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "TestAuth");
            var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
            // Pin base URL parts: the controller encodes {Scheme}://{Host}/{shortCode}.
            httpContext.Request.Scheme = "https";
            httpContext.Request.Host = new HostString("sp.test");

            return new QRCodeController(qrCodeService, db)
            {
                ControllerContext = new ControllerContext { HttpContext = httpContext }
            };
        }

        [Fact]
        public async Task GetQRCode_ShouldReturnPngFile_ForOwner()
        {
            using var db = CreateDb();
            var url = Seed(db, "owner");
            byte[] generated = [9, 8, 7];

            var qr = new Mock<IQRCodeService>();
            qr.Setup(s => s.GenerateQRCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(generated);

            var controller = CreateController(qr.Object, db, "owner");
            var result = await controller.GetQRCode(url.Id, CancellationToken.None);

            var file = result.Should().BeOfType<FileContentResult>().Subject;
            file.ContentType.Should().Be("image/png");
            file.FileContents.Should().Equal(generated);
        }

        [Fact]
        public async Task GetQRCode_ShouldEncodeFullRedirectUrl_ForOwner()
        {
            using var db = CreateDb();
            var url = Seed(db, "owner");

            var qr = new Mock<IQRCodeService>();
            qr.Setup(s => s.GenerateQRCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync([1]);

            var controller = CreateController(qr.Object, db, "owner");
            await controller.GetQRCode(url.Id, CancellationToken.None);

            qr.Verify(s => s.GenerateQRCodeAsync($"https://sp.test/{url.ShortCode}", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetQRCode_ShouldReturn404_AndNotRender_WhenNotOwner()
        {
            using var db = CreateDb();
            var url = Seed(db, "owner");

            var qr = new Mock<IQRCodeService>();
            var controller = CreateController(qr.Object, db, "intruder");

            var result = await controller.GetQRCode(url.Id, CancellationToken.None);

            result.Should().BeOfType<NotFoundResult>();
            qr.Verify(s => s.GenerateQRCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task GetQRCode_ShouldReturn404_ForUnknownId()
        {
            using var db = CreateDb();

            var qr = new Mock<IQRCodeService>();
            var controller = CreateController(qr.Object, db, "owner");

            var result = await controller.GetQRCode(Guid.NewGuid(), CancellationToken.None);

            result.Should().BeOfType<NotFoundResult>();
            qr.Verify(s => s.GenerateQRCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
