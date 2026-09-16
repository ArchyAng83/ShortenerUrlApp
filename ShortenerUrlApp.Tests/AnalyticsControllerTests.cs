using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ShortenerUrlApp.Shared.DTOs;
using ShortenerUrlApp.WebApi.Controllers;
using ShortenerUrlApp.WebApi.Services;
using System.Security.Claims;

namespace ShortenerUrlApp.Tests
{
    public class AnalyticsControllerTests
    {
        private static readonly Guid UrlId = Guid.NewGuid();

        private static AnalyticsController CreateController(
            Mock<IAnalyticsService> analytics,
            ClaimsPrincipal? user = null)
        {
            user ??= new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, "user-1") }));

            return new AnalyticsController(analytics.Object)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = user }
                }
            };
        }

        private static AnalyticsSummaryDto Summary(int totalClicks, List<ClicksByPeriodDto>? period = null) =>
            new(totalClicks,
                period ?? new List<ClicksByPeriodDto>(),
                new List<NameCountDto>(),
                new List<NameCountDto>());

        [Theory]
        [InlineData("hour")]
        [InlineData("year")]
        public async Task GetAnalytics_InvalidGroupBy_ShouldReturnBadRequest(string groupBy)
        {
            var analytics = new Mock<IAnalyticsService>();
            var controller = CreateController(analytics);

            var result = await controller.GetAnalyticsAsync(UrlId, null, null, groupBy);

            result.Should().BeOfType<BadRequestObjectResult>();
            analytics.Verify(s => s.GetAnalyticsAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task GetAnalytics_DayBucket_ShouldNotRebucket()
        {
            var analytics = new Mock<IAnalyticsService>();
            analytics.Setup(s => s.GetAnalyticsAsync(UrlId, "user-1", It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(Summary(5));

            var controller = CreateController(analytics);

            var result = await controller.GetAnalyticsAsync(UrlId, null, null, "day");

            result.Should().BeOfType<OkObjectResult>();
            analytics.Verify(s => s.GetClicksByPeriodAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task GetAnalytics_CoarseBucketWithClicks_ShouldRebucketFromPeriod()
        {
            var rebucketed = new List<ClicksByPeriodDto> { new("2026-09", 5) };
            var analytics = new Mock<IAnalyticsService>();
            analytics.Setup(s => s.GetAnalyticsAsync(UrlId, "user-1", It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(Summary(5));
            analytics.Setup(s => s.GetClicksByPeriodAsync(UrlId, "user-1", DateTime.MinValue, DateTime.MaxValue, "week", It.IsAny<CancellationToken>()))
                     .ReturnsAsync(rebucketed);

            var controller = CreateController(analytics);

            var result = await controller.GetAnalyticsAsync(UrlId, null, null, "week");

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            ok.Value.Should().BeOfType<AnalyticsSummaryDto>()
              .Which.ClicksByPeriod.Should().BeEquivalentTo(rebucketed);
        }

        [Theory]
        [InlineData("week")]
        [InlineData("month")]
        public async Task GetAnalytics_CoarseBucketWithoutClicks_ShouldNotRebucket(string groupBy)
        {
            var analytics = new Mock<IAnalyticsService>();
            analytics.Setup(s => s.GetAnalyticsAsync(UrlId, "user-1", It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(Summary(0));

            var controller = CreateController(analytics);

            await controller.GetAnalyticsAsync(UrlId, null, null, groupBy);

            analytics.Verify(s => s.GetClicksByPeriodAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task GetAnalytics_ShouldResolveUserIdFromSubClaim()
        {
            var analytics = new Mock<IAnalyticsService>();
            analytics.Setup(s => s.GetAnalyticsAsync(UrlId, "user-2", It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(Summary(0));

            var subUser = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", "user-2") }));
            var controller = CreateController(analytics, subUser);

            await controller.GetAnalyticsAsync(UrlId, null, null);

            analytics.VerifyAll();
        }

        [Fact]
        public async Task GetClicksByCountry_ShouldReturnOk()
        {
            var analytics = new Mock<IAnalyticsService>();
            analytics.Setup(s => s.GetClicksByCountryAsync(UrlId, "user-1", It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new Dictionary<string, int> { ["GB"] = 3 });

            var controller = CreateController(analytics);

            var result = await controller.GetClicksByCountryAsync(UrlId, CancellationToken.None);

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            ok.Value.Should().BeEquivalentTo(new Dictionary<string, int> { ["GB"] = 3 });
        }

        [Fact]
        public async Task GetTopReferrers_DefaultTop_ShouldBeTen()
        {
            var analytics = new Mock<IAnalyticsService>();
            analytics.Setup(s => s.GetTopReferrersAsync(UrlId, "user-1", 10, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new Dictionary<string, int> { ["google.com"] = 7 });

            var controller = CreateController(analytics);

            await controller.GetTopReferrersAsync(UrlId);

            analytics.VerifyAll();
        }

        [Theory]
        [InlineData(0)]
        [InlineData(101)]
        public async Task GetTopReferrers_OutOfRangeTop_ShouldReturnBadRequest(int top)
        {
            var analytics = new Mock<IAnalyticsService>();
            var controller = CreateController(analytics);

            var result = await controller.GetTopReferrersAsync(UrlId, top);

            result.Should().BeOfType<BadRequestObjectResult>();
            analytics.Verify(s => s.GetTopReferrersAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
