using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ShortenerUrlApp.Shared.DTOs;
using ShortenerUrlApp.WebApi.Data;
using ShortenerUrlApp.WebApi.Entities;
using ShortenerUrlApp.WebApi.Services;

namespace ShortenerUrlApp.Tests
{
    public class AnalyticsServiceTests
    {
        private static ShortenerUrlDbContext CreateDb() =>
            new(new DbContextOptionsBuilder<ShortenerUrlDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        private static ShortenerUrl SeedUrl(ShortenerUrlDbContext db, string? userId, string shortCode = "aaaa111")
        {
            var url = new ShortenerUrl
            {
                LongUrl = "https://example.com",
                ShortCode = shortCode,
                UserId = userId
            };
            db.ShortenerUrls.Add(url);
            db.SaveChanges();
            return url;
        }

        private static void SeedClicks(
            ShortenerUrlDbContext db, Guid urlId,
            params (DateTime ClickedAt, string? Referrer, string? Country)[] clicks)
        {
            db.ClickEvents.AddRange(clicks.Select(c => new ClickEvent
            {
                Id = Guid.NewGuid(),
                ShortenerUrlId = urlId,
                ClickedAt = c.ClickedAt,
                Referrer = c.Referrer,
                Country = c.Country
            }));
            db.SaveChanges();
        }

        private static AnalyticsService CreateService(ShortenerUrlDbContext db) => new(db);

        [Fact]
        public async Task GetAnalyticsAsync_ShouldReturnFullSummary_ForOwner()
        {
            // Arrange
            var db = CreateDb();
            var url = SeedUrl(db, "owner");
            SeedClicks(db, url.Id,
                (new DateTime(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc), "https://news.a", "DE"),
                (new DateTime(2026, 9, 10, 9, 30, 0, DateTimeKind.Utc), "https://news.a", "DE"),
                (new DateTime(2026, 9, 10, 23, 59, 0, DateTimeKind.Utc), "https://news.a", "US"),
                (new DateTime(2026, 9, 11, 5, 0, 0, DateTimeKind.Utc), "https://blog.b", "US"));

            var service = CreateService(db);

            // Act
            var summary = await service.GetAnalyticsAsync(url.Id, "owner", null, null, CancellationToken.None);

            // Assert
            summary.TotalClicks.Should().Be(4);

            // Daily buckets: clicks at 00:00 boundary land in their own calendar day.
            summary.ClicksByPeriod.Should().Equal(
                new ClicksByPeriodDto("2026-09-10", 3),
                new ClicksByPeriodDto("2026-09-11", 1));

            summary.TopReferrers.Should().Equal(
                new NameCountDto("https://news.a", 3),
                new NameCountDto("https://blog.b", 1));

            summary.ClicksByCountry.Should().Equal(
                // Ties in click count are broken alphabetically (ThenBy in the service).
                new NameCountDto("DE", 2),
                new NameCountDto("US", 2));
        }

        [Fact]
        public async Task GetAnalyticsAsync_ShouldRespectDateRange()
        {
            var db = CreateDb();
            var url = SeedUrl(db, "owner");
            SeedClicks(db, url.Id,
                (new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc), null, null),
                (new DateTime(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc), null, null),
                (new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc), null, null));

            var service = CreateService(db);

            var summary = await service.GetAnalyticsAsync(
                url.Id, "owner",
                new DateTime(2026, 9, 4, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 9, 6, 0, 0, 0, DateTimeKind.Utc),
                CancellationToken.None);

            summary.TotalClicks.Should().Be(1);
            summary.ClicksByPeriod.Should().Equal(new ClicksByPeriodDto("2026-09-05", 1));
        }

        [Theory]
        [InlineData("intruder")]
        [InlineData(null)] // anonymous caller must not see a registered user's analytics
        public async Task GetAnalyticsAsync_ShouldReturnEmptySummary_ForNonOwner(string? userId)
        {
            var db = CreateDb();
            var url = SeedUrl(db, "owner");
            SeedClicks(db, url.Id, (DateTime.UtcNow, "https://news.a", "DE"));

            var service = CreateService(db);

            var summary = await service.GetAnalyticsAsync(url.Id, userId, null, null, CancellationToken.None);

            summary.TotalClicks.Should().Be(0);
            summary.ClicksByPeriod.Should().BeEmpty();
            summary.TopReferrers.Should().BeEmpty();
            summary.ClicksByCountry.Should().BeEmpty();
        }

        [Fact]
        public async Task GetAnalyticsAsync_ShouldReturnEmptySummary_ForUnknownUrl()
        {
            var db = CreateDb();
            var service = CreateService(db);

            var summary = await service.GetAnalyticsAsync(Guid.NewGuid(), "owner", null, null, CancellationToken.None);

            summary.TotalClicks.Should().Be(0);
        }

        [Theory]
        // Calendar anchors: 2026-09-14 is a Monday, so 2026-09-15 is Tuesday and 2026-09-12 is Saturday.
        // With Monday-start weeks: Tue/Mon share the "2026-09-14" bucket, Sat falls into "2026-09-07",
        // and 2026-08-20 (Thu) is in both another week and another month.
        [InlineData("day", "2026-09-15", 1)]
        [InlineData("week", "2026-09-14", 2)]
        [InlineData("month", "2026-09", 3)]
        public async Task GetClicksByPeriodAsync_ShouldGroupByRequestedGranularity(string groupBy, string expectedBucket, int expectedSameBucket)
        {
            var db = CreateDb();
            var url = SeedUrl(db, "owner");
            SeedClicks(db, url.Id,
                (new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc), null, null),   // Tue
                (new DateTime(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc), null, null),   // Mon — same week, other day
                (new DateTime(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc), null, null),   // Sat — week 2026-09-07, same month
                (new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc), null, null));  // Thu — other week, other month

            var service = CreateService(db);

            var buckets = await service.GetClicksByPeriodAsync(
                url.Id, "owner",
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
                groupBy, CancellationToken.None);

            buckets.Should().Contain(b => b.Date == expectedBucket);
            buckets.First(b => b.Date == expectedBucket).Count.Should().Be(expectedSameBucket);
        }

        [Fact]
        public async Task GetClicksByPeriodAsync_Weekly_ShouldBucketMondayStart()
        {
            var db = CreateDb();
            var url = SeedUrl(db, "owner");
            SeedClicks(db, url.Id,
                (new DateTime(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc), null, null),  // Mon
                (new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc), null, null),  // Tue, same week
                (new DateTime(2026, 9, 13, 0, 0, 0, DateTimeKind.Utc), null, null)); // Sun, previous week

            var service = CreateService(db);

            var buckets = await service.GetClicksByPeriodAsync(
                url.Id, "owner",
                new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc),
                "week", CancellationToken.None);

            buckets.Should().Equal(
                new ClicksByPeriodDto("2026-09-07", 1),
                new ClicksByPeriodDto("2026-09-14", 2));
        }

        [Fact]
        public async Task GetClicksByPeriodAsync_Monthly_ShouldUseYearMonthFormat()
        {
            var db = CreateDb();
            var url = SeedUrl(db, "owner");
            SeedClicks(db, url.Id,
                (new DateTime(2026, 8, 31, 23, 0, 0, DateTimeKind.Utc), null, null),
                (new DateTime(2026, 9, 1, 1, 0, 0, DateTimeKind.Utc), null, null),
                (new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc), null, null));

            var service = CreateService(db);

            var buckets = await service.GetClicksByPeriodAsync(
                url.Id, "owner",
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
                "month", CancellationToken.None);

            buckets.Should().Equal(
                new ClicksByPeriodDto("2026-08", 1),
                new ClicksByPeriodDto("2026-09", 2));
        }

        [Fact]
        public async Task GetClicksByPeriodAsync_ShouldReturnEmpty_ForNonOwner()
        {
            var db = CreateDb();
            var url = SeedUrl(db, "owner");
            SeedClicks(db, url.Id, (new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc), null, null));

            var service = CreateService(db);

            var buckets = await service.GetClicksByPeriodAsync(
                url.Id, "intruder",
                new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc),
                "day", CancellationToken.None);

            buckets.Should().BeEmpty();
        }

        [Fact]
        public async Task GetClicksByCountryAsync_ShouldCountByCountry_ForOwner()
        {
            var db = CreateDb();
            var url = SeedUrl(db, "owner");
            SeedClicks(db, url.Id,
                (DateTime.UtcNow, null, "DE"),
                (DateTime.UtcNow, null, "DE"),
                (DateTime.UtcNow, null, "FR"),
                (DateTime.UtcNow, null, null)); // null country is excluded from the breakdown

            var service = CreateService(db);

            var result = await service.GetClicksByCountryAsync(url.Id, "owner", CancellationToken.None);

            result.Should().Equal(new Dictionary<string, int> { ["DE"] = 2, ["FR"] = 1 });
        }

        [Fact]
        public async Task GetClicksByCountryAsync_ShouldReturnEmpty_ForNonOwner()
        {
            var db = CreateDb();
            var url = SeedUrl(db, "owner");
            SeedClicks(db, url.Id, (DateTime.UtcNow, null, "DE"));

            var service = CreateService(db);

            var result = await service.GetClicksByCountryAsync(url.Id, "intruder", CancellationToken.None);

            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetTopReferrersAsync_ShouldLimitAndOrderDescending_ForOwner()
        {
            var db = CreateDb();
            var url = SeedUrl(db, "owner");
            SeedClicks(db, url.Id,
                (DateTime.UtcNow, "https://a", null),
                (DateTime.UtcNow, "https://a", null),
                (DateTime.UtcNow, "https://a", null),
                (DateTime.UtcNow, "https://b", null),
                (DateTime.UtcNow, null, null)); // null referrer is excluded

            var service = CreateService(db);

            var result = await service.GetTopReferrersAsync(url.Id, "owner", 1, CancellationToken.None);

            result.Should().HaveCount(1);
            result.Keys.Should().ContainSingle().Which.Should().Be("https://a");
            result["https://a"].Should().Be(3);
        }

        [Fact]
        public async Task GetTopReferrersAsync_ShouldReturnEmpty_ForNonOwner()
        {
            var db = CreateDb();
            var url = SeedUrl(db, "owner");
            SeedClicks(db, url.Id, (DateTime.UtcNow, "https://a", null));

            var service = CreateService(db);

            var result = await service.GetTopReferrersAsync(url.Id, "intruder", 10, CancellationToken.None);

            result.Should().BeEmpty();
        }
    }
}
