using ShortenerUrlApp.WebApi.Entities;

namespace ShortenerUrlApp.WebApi.Services
{
    public interface IShortenerUrlService
    {
        Task<string> ShortenUrlAsync(string longUrl, string? userId, CancellationToken ct);
        Task<string> GetLongUrlAsync(string shortCode, CancellationToken ct);
        Task<List<ShortenerUrl>> GetAllUrlsAsync(string? userId, CancellationToken ct);
        Task<bool> UpdateUrlAsync(Guid id, string newLongUrl, string? userId, CancellationToken ct);
        Task<bool> DeleteUrlAsync(Guid id, string? userId, CancellationToken ct);
        Task SyncClicksToDbAsync(CancellationToken ct);
    }
}
