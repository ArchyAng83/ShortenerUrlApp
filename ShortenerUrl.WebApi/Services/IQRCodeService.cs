namespace ShortenerUrlApp.WebApi.Services
{
    /// <summary>
    /// Renders a PNG QR code for a given absolute URL (a link's full redirect target).
    /// Generated images are cached in Redis keyed by the SHA-256 hash of the encoded
    /// URL, so repeated requests never re-render the bitmap.
    /// </summary>
    public interface IQRCodeService
    {
        Task<byte[]> GenerateQRCodeAsync(string url, CancellationToken ct = default);
    }
}
