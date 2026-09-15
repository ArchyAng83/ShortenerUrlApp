using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShortenerUrlApp.Shared.DTOs;
using ShortenerUrlApp.WebApi.Services;
using System.Security.Claims;

namespace ShortenerUrlApp.WebApi.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class ShortenerUrlController(IShortenerUrlService shortenerService) : ControllerBase
    {
        [HttpGet]
        public async Task<IActionResult> GetAllUrlsAsync(CancellationToken ct)
        {
            var shortenerUrls = await shortenerService.GetAllUrlsAsync(GetUserId(), ct);

            var response = shortenerUrls.Select(u => new UrlResposeDto(
                u.Id,
                u.LongUrl,
                $"{Request.Scheme}://{Request.Host}/{u.ShortCode}",
                u.CreateAt,
                u.CountOfClick));

            return Ok(response);
        }

        [HttpPost]
        public async Task<IActionResult> CreateShortUrlAsync([FromBody] CreateShortUrlDto shortUrlDto, CancellationToken ct)
        {
            if (!CheckUrl(shortUrlDto.LongUrl))
            {
                return BadRequest("Invalid reference!");
            }

            var code = await shortenerService.ShortenUrlAsync(shortUrlDto.LongUrl, GetUserId(), ct);

            return Ok(code);
        }

        [HttpPut]
        public async Task<IActionResult> UpdateLongUrlAsync([FromBody] UpdateLongUrlDto longUrlDto, CancellationToken ct)
        {
            if (!CheckUrl(longUrlDto.LongUrl))
            {
                return BadRequest("Invalid reference!");
            }

            var updated = await shortenerService.UpdateUrlAsync(longUrlDto.Id, longUrlDto.LongUrl, GetUserId(), ct);

            return updated ? Ok(longUrlDto) : NotFound();
        }

        [HttpDelete("{id:guid}")]
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
            if (!Uri.TryCreate(longUrl, UriKind.Absolute, out var uriResult)
                || (uriResult.Scheme != Uri.UriSchemeHttp && uriResult.Scheme != Uri.UriSchemeHttps))
            {
                return false;
            }

            return true;
        }
    }
}
