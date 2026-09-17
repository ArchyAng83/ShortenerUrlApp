using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ShortenerUrlApp.Shared.DTOs;
using ShortenerUrlApp.WebApi.Constants;
using ShortenerUrlApp.WebApi.Services;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace ShortenerUrlApp.WebApi.Controllers
{
    [Authorize]
    [Route("api/v1/urls")]
    [ApiController]
    [EnableRateLimiting("global")]
    public partial class ShortenerUrlController(IShortenerUrlService shortenerService) : ControllerBase
    {
        // Codes that would collide with app routes (/{code} redirect vs /health, /api, /openapi, /scalar).
        private static readonly HashSet<string> ReservedAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            "health", "api", "openapi", "swagger", "scalar"
        };

        [HttpGet]
        public async Task<IActionResult> GetAllUrlsAsync(CancellationToken ct)
        {
            var shortenerUrls = await shortenerService.GetAllUrlsAsync(GetUserId(), ct);

            var now = DateTime.UtcNow;

            // Batch-fetch pending Redis clicks for all URLs in one pipeline round-trip.
            var pendingTasks = shortenerUrls
                .Select(u => shortenerService.GetPendingClicksAsync(u.ShortCode, ct))
                .ToList();
            var pendingClicks = await Task.WhenAll(pendingTasks);

            var response = shortenerUrls.Select((u, i) => new UrlResponseDto(
                u.Id,
                u.LongUrl,
                $"{Request.Scheme}://{Request.Host}/{u.ShortCode}",
                u.CreateAt,
                u.CountOfClick + pendingClicks[i])
            {
                ExpiresAt = u.ExpiresAt,
                IsCustomAlias = u.IsCustomAlias,
                MaxClicks = u.MaxClicks,
                IsExpired = u.ExpiresAt.HasValue && u.ExpiresAt < now
            });

            return Ok(response);
        }

        [HttpPost]
        [EnableRateLimiting("url_create")]
        public async Task<IActionResult> CreateShortUrlAsync([FromBody] CreateShortUrlDto shortUrlDto, CancellationToken ct)
        {
            // LongUrl is [Required], so a null never survives model binding; the guard
            // keeps the nullable annotation honest and defensive.
            if (shortUrlDto.LongUrl is null || !CheckUrl(shortUrlDto.LongUrl))
            {
                return BadRequest("Invalid reference!");
            }

            // The DTO attributes already enforce length/charset via ModelState; this adds
            // the routing-safety check (reserved names) that data annotations cannot express.
            var alias = shortUrlDto.CustomAlias?.Trim();

            if (alias is not null)
            {
                if (!CheckAlias(alias))
                {
                    return BadRequest($"Invalid alias: {Constant.MIN_LENGTH_CUSTOM_ALIAS}-{Constant.MAX_LENGTH_CUSTOM_ALIAS} chars of [a-zA-Z0-9_-].");
                }

                if (ReservedAliases.Contains(alias))
                {
                    return BadRequest("This alias is reserved.");
                }
            }

            try
            {
                var code = await shortenerService.ShortenUrlAsync(
                    shortUrlDto.LongUrl,
                    GetUserId(),
                    alias,
                    shortUrlDto.ExpiresInMinutes,
                    shortUrlDto.MaxClicks,
                    ct);

                return Ok(code);
            }
            catch (AliasAlreadyInUseException ex)
            {
                return Conflict(ex.Message);
            }
        }

        [HttpPut]
        [EnableRateLimiting("url_create")]
        public async Task<IActionResult> UpdateLongUrlAsync([FromBody] UpdateLongUrlDto longUrlDto, CancellationToken ct)
        {
            if (longUrlDto.LongUrl is null || !CheckUrl(longUrlDto.LongUrl))
            {
                return BadRequest("Invalid reference!");
            }

            var updated = await shortenerService.UpdateUrlAsync(longUrlDto.Id, longUrlDto.LongUrl, GetUserId(), ct);

            return updated ? Ok(longUrlDto) : NotFound();
        }

        [HttpDelete("{id:guid}")]
        [EnableRateLimiting("url_create")]
        public async Task<IActionResult> DeleteUrlAsync(Guid id, CancellationToken ct)
        {
            var deleted = await shortenerService.DeleteUrlAsync(id, GetUserId(), ct);

            return deleted ? NoContent() : NotFound();
        }

        // NameIdentifier covers the mapped "sub" claim; "sub" covers MapInboundClaims=false.
        private string? GetUserId() =>
            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");

        private static bool CheckUrl(string longUrl)
        {
            return ShortenerUrlService.IsUrlSafe(longUrl);
        }

        [GeneratedRegex(@"^[a-zA-Z0-9_-]+$")]
        private static partial Regex AliasRegex();

        // Format rule for user-supplied aliases; mirrors the DTO attributes so the
        // service can reuse the same contract if validation is needed server-side.
        public static bool CheckAlias(string alias) =>
            alias.Length >= Constant.MIN_LENGTH_CUSTOM_ALIAS
            && alias.Length <= Constant.MAX_LENGTH_CUSTOM_ALIAS
            && AliasRegex().IsMatch(alias);
    }
}
