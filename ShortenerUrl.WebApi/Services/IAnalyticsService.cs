using ShortenerUrlApp.Shared.DTOs;

namespace ShortenerUrlApp.WebApi.Services
{
    /// <summary>
    /// Read-only analytics over stored <c>ClickEvent</c> rows.
    /// Every method verifies that the URL belongs to <paramref name="userId"/> first;
    /// foreign or missing URLs yield empty/zero results so the API never leaks their existence.
    /// </summary>
    public interface IAnalyticsService
    {
        Task<AnalyticsSummaryDto> GetAnalyticsAsync(
            Guid urlId, string? userId, DateTime? dateFrom, DateTime? dateTo, CancellationToken ct = default);

        Task<List<ClicksByPeriodDto>> GetClicksByPeriodAsync(
            Guid urlId, string? userId, DateTime dateFrom, DateTime dateTo, string groupBy, CancellationToken ct = default);

        Task<Dictionary<string, int>> GetClicksByCountryAsync(
            Guid urlId, string? userId, CancellationToken ct = default);

        Task<Dictionary<string, int>> GetTopReferrersAsync(
            Guid urlId, string? userId, int top, CancellationToken ct = default);
    }
}
