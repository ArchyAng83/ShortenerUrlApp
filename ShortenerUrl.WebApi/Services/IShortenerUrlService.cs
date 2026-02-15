using ShortenerUrlApp.WebApi.Entities;

namespace ShortenerUrlApp.WebApi.Services
{
    public interface IShortenerUrlService
    {
        Task<string> ShortenUrlAsync(string longUrl, CancellationToken ct);
        Task<string> GetLongUrlAsync(string shortCode, CancellationToken ct);
        Task<List<ShortenerUrl>> GetAllUrlsAsync(CancellationToken ct);
        Task UpdateUrlAsync(Guid id, string newLongUrl, CancellationToken ct);
        Task DeleteUrlAsync(Guid id, CancellationToken ct);
    }
}
