using ShortenerUrlApp.WebApi.Entities;

namespace ShortenerUrlApp.WebApi.Services
{
    public interface IShortenerUrlService
    {
        // Legacy overload used by existing callers: random code, no expiry, no click cap.
        Task<string> ShortenUrlAsync(string longUrl, string? userId, CancellationToken ct);

        Task<string> ShortenUrlAsync(
            string longUrl,
            string? userId,
            string? customAlias = null,
            int? expiresInMinutes = null,
            int? maxClicks = null,
            CancellationToken ct = default);

        Task<string> GetLongUrlAsync(string shortCode, CancellationToken ct);

        // Status-aware resolution for the redirect endpoint: distinguishes 404 from 410 (Gone).
        Task<RedirectResult> GetLongUrlWithStatusAsync(string shortCode, CancellationToken ct = default);

        Task<List<ShortenerUrl>> GetAllUrlsAsync(string? userId, CancellationToken ct);
        Task<bool> UpdateUrlAsync(Guid id, string newLongUrl, string? userId, CancellationToken ct);
        Task<bool> DeleteUrlAsync(Guid id, string? userId, CancellationToken ct);
        Task SyncClicksToDbAsync(CancellationToken ct);

        // Removes links whose ExpiresAt has passed and evicts their Redis keys. Returns the deleted row count.
        Task<int> DeleteExpiredUrlsAsync(CancellationToken ct = default);
    }
}
