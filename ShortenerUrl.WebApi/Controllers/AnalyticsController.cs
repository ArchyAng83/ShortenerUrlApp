using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShortenerUrlApp.Shared.DTOs;
using ShortenerUrlApp.WebApi.Services;
using System.Security.Claims;

namespace ShortenerUrlApp.WebApi.Controllers
{
    /// <summary>
    /// Click analytics for a single shortened URL. Only the link owner can read the stats;
    /// foreign or unknown URLs return empty results (never 403/404) so existence is not leaked.
    /// </summary>
    [Authorize]
    [Route("api/v1/urls/{urlId:guid}/analytics")]
    [ApiController]
    public class AnalyticsController(IAnalyticsService analytics) : ControllerBase
    {
        [HttpGet]
        public async Task<IActionResult> GetAnalyticsAsync(
            Guid urlId,
            [FromQuery] DateTime? dateFrom,
            [FromQuery] DateTime? dateTo,
            [FromQuery] string groupBy = "day",
            CancellationToken ct = default)
        {
            if (groupBy is not ("day" or "week" or "month"))
            {
                return BadRequest("groupBy must be one of: day, week, month.");
            }

            var userId = GetUserId();
            var summary = await analytics.GetAnalyticsAsync(urlId, userId, dateFrom, dateTo, ct);

            // The summary series is daily by default; recompute it when a coarser bucket was requested.
            if (summary.TotalClicks > 0 && groupBy != "day")
            {
                var period = await analytics.GetClicksByPeriodAsync(
                    urlId, userId, dateFrom ?? DateTime.MinValue, dateTo ?? DateTime.MaxValue, groupBy, ct);

                summary = summary with { ClicksByPeriod = period };
            }

            return Ok(summary);
        }

        [HttpGet("by-country")]
        public async Task<IActionResult> GetClicksByCountryAsync(Guid urlId, CancellationToken ct)
        {
            var clicksByCountry = await analytics.GetClicksByCountryAsync(urlId, GetUserId(), ct);

            return Ok(clicksByCountry);
        }

        [HttpGet("by-referrers")]
        public async Task<IActionResult> GetTopReferrersAsync(
            Guid urlId, [FromQuery] int top = 10, CancellationToken ct = default)
        {
            if (top is < 1 or > 100)
            {
                return BadRequest("top must be between 1 and 100.");
            }

            var referrers = await analytics.GetTopReferrersAsync(urlId, GetUserId(), top, ct);

            return Ok(referrers);
        }

        // NameIdentifier covers the mapped "sub" claim; "sub" covers MapInboundClaims=false.
        private string? GetUserId() =>
            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
    }
}
