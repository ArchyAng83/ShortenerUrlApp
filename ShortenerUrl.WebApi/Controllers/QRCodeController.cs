using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShortenerUrlApp.WebApi.Data;
using ShortenerUrlApp.WebApi.Services;
using System.Security.Claims;

namespace ShortenerUrlApp.WebApi.Controllers
{
    /// <summary>
    /// PNG QR code for a link's full redirect URL. Ownership is verified before any
    /// rendering happens; foreign or unknown links return 404 so existence is not leaked.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/v1/urls/{urlId:guid}/qrcode")]
    public class QRCodeController(IQRCodeService qrCodeService, ShortenerUrlDbContext context) : ControllerBase
    {
        [HttpGet]
        public async Task<IActionResult> GetQRCode(Guid urlId, CancellationToken ct)
        {
            var shortenerUrl = await context.ShortenerUrls
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == urlId, ct);

            // Treat foreign URLs as "not found" so the API does not leak their existence.
            if (shortenerUrl is null || shortenerUrl.UserId != GetUserId())
            {
                return NotFound();
            }

            // Encode the exact short URL the dashboard shows and the /{code} redirect
            // route serves (ShortenerUrlController builds it the same way from the request).
            var redirectUrl = $"{Request.Scheme}://{Request.Host}/{shortenerUrl.ShortCode}";

            byte[] png = await qrCodeService.GenerateQRCodeAsync(redirectUrl, ct);

            return File(png, "image/png");
        }

        // NameIdentifier covers the mapped "sub" claim; "sub" covers MapInboundClaims=false.
        private string? GetUserId() =>
            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
    }
}
