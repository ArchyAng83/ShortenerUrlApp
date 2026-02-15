using Microsoft.AspNetCore.Mvc;
using ShortenerUrlApp.WebApi.DTOs;
using ShortenerUrlApp.WebApi.Services;

namespace ShortenerUrlApp.WebApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ShortenerUrlController(IShortenerUrlService shortenerService) : ControllerBase
    {
        [HttpGet]
        public async Task<IActionResult> GetAllUrlsAsync(CancellationToken ct)
        {
            var shortenerUrls = await shortenerService.GetAllUrlsAsync(ct);

            var response = shortenerUrls.Select(u => new UrlResposeDto(
                u.Id,
                u.LongUrl,
                $"{Request.Scheme}://{u.ShortCode}",
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

            var code = await shortenerService.ShortenUrlAsync(shortUrlDto.LongUrl, ct);

            return Ok(code);
        }

        [HttpPut]
        public async Task<IActionResult> UpdateLongUrlAsync([FromBody] UpdateLongUrlDto longUrlDto, CancellationToken ct)
        {
            if (!CheckUrl(longUrlDto.LongUrl))
            {
                return BadRequest("Invalid reference!");
            }

            await shortenerService.UpdateUrlAsync(longUrlDto.Id, longUrlDto.LongUrl, ct);

            return Ok(longUrlDto);
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> DeleteUrlAsync(Guid id, CancellationToken ct)
        {
            await shortenerService.DeleteUrlAsync(id, ct);

            return NoContent();
        }

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
