using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ShortenerUrlApp.WebApi.Data;
using ShortenerUrlApp.WebApi.Entities;
using ShortenerUrlApp.WebApi.Services;
using StackExchange.Redis;
using System.Net;

namespace ShortenerUrlApp.Tests
{
    public class ClickEventSyncWorkerTests
    {
        private const string Key = "click-events:abc123";

        private static Mock<IConnectionMultiplexer> BuildRedis(
            RedisKey[] keys, RedisValue[] range, bool execute = true)
        {
            var tx = new Mock<ITransaction>();
            tx.Setup(t => t.ListRangeAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
              .ReturnsAsync(range);
            tx.Setup(t => t.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
              .ReturnsAsync(true);
            tx.Setup(t => t.ExecuteAsync(It.IsAny<CommandFlags>()))
              .ReturnsAsync(execute);

            var db = new Mock<IDatabase>();
            db.Setup(d => d.CreateTransaction(It.IsAny<object>())).Returns(tx.Object);

            var server = new Mock<IServer>();
            server.Setup(s => s.Keys(It.IsAny<int>(), It.IsAny<RedisValue>(), It.IsAny<int>(), It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CommandFlags>()))
                  .Returns(keys);

            var redis = new Mock<IConnectionMultiplexer>();
            redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(db.Object);
            redis.Setup(r => r.GetEndPoints(It.IsAny<bool>()))
                 .Returns(new EndPoint[] { new IPEndPoint(IPAddress.Loopback, 6379) });
            redis.Setup(r => r.GetServer(It.IsAny<EndPoint>(), It.IsAny<object>())).Returns(server.Object);

            return redis;
        }

        private static Mock<IGeoIpService> BuildGeo() => new();

        private static ServiceProvider BuildProvider(
            Mock<IConnectionMultiplexer> redis,
            Mock<IGeoIpService> geo,
            bool seedUrl = true)
        {
            // Capture the store name once: AddDbContext re-applies the options action per
            // scope, so a Guid inside the lambda would spawn a fresh InMemory store per scope.
            var databaseName = Guid.NewGuid().ToString();
            var services = new ServiceCollection();
            services.AddDbContext<ShortenerUrlDbContext>(o => o.UseInMemoryDatabase(databaseName));
            services.AddSingleton((IConnectionMultiplexer)redis.Object);
            services.AddSingleton(geo.Object);

            var provider = services.BuildServiceProvider();

            if (seedUrl)
            {
                using var scope = provider.CreateScope();
                var ctx = scope.ServiceProvider.GetRequiredService<ShortenerUrlDbContext>();
                ctx.ShortenerUrls.Add(new ShortenerUrl
                {
                    Id = Guid.NewGuid(),
                    LongUrl = "https://example.com",
                    ShortCode = "abc123"
                });
                ctx.SaveChanges();
            }

            return provider;
        }

        private static async Task SyncAsync(ServiceProvider provider)
        {
            using var scope = provider.CreateScope();
            await ClickEventSyncWorker.SyncAsync(scope.ServiceProvider, CancellationToken.None);
        }

        private static async Task<List<ClickEvent>> GetSavedEvents(ServiceProvider provider)
        {
            using var scope = provider.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<ShortenerUrlDbContext>();
            return await ctx.ClickEvents.ToListAsync();
        }

        [Fact]
        public async Task SyncAsync_ShouldDrainAndEnrichClickFromGeoIp()
        {
            const string json =
                """{"clickedAt":"2026-09-17T12:00:00Z","ipAddress":"81.2.69.142","userAgent":"UA","referrer":"ref"}""";
            var redis = BuildRedis(new[] { (RedisKey)Key }, new[] { (RedisValue)json });
            var geo = BuildGeo();
            geo.Setup(g => g.Resolve("81.2.69.142")).Returns(new GeoIpLocation("GB", "London"));

            using var provider = BuildProvider(redis, geo);

            await SyncAsync(provider);

            var events = await GetSavedEvents(provider);
            var click = events.Should().ContainSingle().Subject;
            click.Country.Should().Be("GB");
            click.City.Should().Be("London");
            click.IpAddress.Should().Be("81.2.69.142");
            click.UserAgent.Should().Be("UA");
            click.Referrer.Should().Be("ref");
            click.ShortenerUrlId.Should().NotBeEmpty();
        }

        [Fact]
        public async Task SyncAsync_ShouldResolveGeoIpOncePerUniqueIp()
        {
            const string json1 =
                """{"clickedAt":"2026-09-17T12:00:00Z","ipAddress":"81.2.69.142","userAgent":"UA","referrer":"a"}""";
            const string json2 =
                """{"clickedAt":"2026-09-17T13:00:00Z","ipAddress":"81.2.69.142","userAgent":"UA2","referrer":"b"}""";
            var redis = BuildRedis(new[] { (RedisKey)Key }, new[] { (RedisValue)json1, (RedisValue)json2 });
            var geo = BuildGeo();
            geo.Setup(g => g.Resolve("81.2.69.142")).Returns(new GeoIpLocation("GB", "London"));

            using var provider = BuildProvider(redis, geo);
            await SyncAsync(provider);

            var events = await GetSavedEvents(provider);
            events.Should().HaveCount(2);
            geo.Verify(g => g.Resolve("81.2.69.142"), Times.Once);
        }

        [Fact]
        public async Task SyncAsync_ShouldPersistWithoutGeo_WhenIpIsNull()
        {
            const string json =
                """{"clickedAt":"2026-09-17T12:00:00Z","userAgent":"UA","referrer":"ref"}""";
            var redis = BuildRedis(new[] { (RedisKey)Key }, new[] { (RedisValue)json });
            var geo = BuildGeo();

            using var provider = BuildProvider(redis, geo);
            await SyncAsync(provider);

            var click = (await GetSavedEvents(provider)).Should().ContainSingle().Subject;
            click.Country.Should().BeNull();
            click.City.Should().BeNull();
            geo.Verify(g => g.Resolve(It.IsAny<string?>()), Times.Never);
        }

        [Fact]
        public async Task SyncAsync_ShouldSkipMalformedEntries()
        {
            var redis = BuildRedis(new[] { (RedisKey)Key }, new[] { (RedisValue)"not-json" });
            var geo = BuildGeo();

            using var provider = BuildProvider(redis, geo);
            await SyncAsync(provider);

            (await GetSavedEvents(provider)).Should().BeEmpty();
            geo.Verify(g => g.Resolve(It.IsAny<string?>()), Times.Never);
        }

        [Fact]
        public async Task SyncAsync_ShouldDropClicksForUnknownShortCode()
        {
            const string json =
                """{"clickedAt":"2026-09-17T12:00:00Z","ipAddress":"81.2.69.142","userAgent":"UA","referrer":"ref"}""";
            var redis = BuildRedis(new[] { (RedisKey)"click-events:deleted" }, new[] { (RedisValue)json });
            var geo = BuildGeo();
            geo.Setup(g => g.Resolve(It.IsAny<string>())).Returns(new GeoIpLocation("US", "Miami"));

            using var provider = BuildProvider(redis, geo);
            await SyncAsync(provider);

            (await GetSavedEvents(provider)).Should().BeEmpty();
            geo.Verify(g => g.Resolve(It.IsAny<string?>()), Times.Never);
        }

        [Fact]
        public async Task SyncAsync_ShouldSkipKey_WhenTransactionFails()
        {
            const string json =
                """{"clickedAt":"2026-09-17T12:00:00Z","ipAddress":"81.2.69.142","userAgent":"UA","referrer":"ref"}""";
            var redis = BuildRedis(new[] { (RedisKey)Key }, new[] { (RedisValue)json }, execute: false);
            var geo = BuildGeo();

            using var provider = BuildProvider(redis, geo);
            await SyncAsync(provider);

            (await GetSavedEvents(provider)).Should().BeEmpty();
        }

        [Fact]
        public async Task SyncAsync_ShouldDoNothing_WhenNoKeysExist()
        {
            var redis = BuildRedis(Array.Empty<RedisKey>(), Array.Empty<RedisValue>());
            var geo = BuildGeo();

            using var provider = BuildProvider(redis, geo);
            await SyncAsync(provider);

            (await GetSavedEvents(provider)).Should().BeEmpty();
            geo.Verify(g => g.Resolve(It.IsAny<string?>()), Times.Never);
        }

        [Fact]
        public async Task RunOneSyncAsync_ShouldSwallowExceptions()
        {
            // Provider has no IConnectionMultiplexer registered -> SyncAsync throws,
            // RunOneSyncAsync must swallow it instead of crashing the worker loop.
            var services = new ServiceCollection();
            services.AddDbContext<ShortenerUrlDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
            services.AddSingleton(BuildGeo().Object);
            using var provider = services.BuildServiceProvider();

            using var scope = provider.CreateScope();
            await ClickEventSyncWorker.RunOneSyncAsync(scope.ServiceProvider, CancellationToken.None);
        }
    }
}
