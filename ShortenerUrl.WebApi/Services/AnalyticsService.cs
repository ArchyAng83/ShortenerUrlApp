using Microsoft.EntityFrameworkCore;
using ShortenerUrlApp.Shared.DTOs;
using ShortenerUrlApp.WebApi.Data;
using ShortenerUrlApp.WebApi.Entities;
using System.Globalization;

namespace ShortenerUrlApp.WebApi.Services
{
    /// <summary>
    /// Aggregates <see cref="ClickEvent"/> rows with server-side EF Core GroupBy queries,
    /// so PostgreSQL — not the API process — does the heavy lifting on large click volumes.
    /// </summary>
    public class AnalyticsService(ShortenerUrlDbContext context) : IAnalyticsService
    {
        public async Task<AnalyticsSummaryDto> GetAnalyticsAsync(
            Guid urlId, string? userId, DateTime? dateFrom, DateTime? dateTo, CancellationToken ct = default)
        {
            if (!await IsOwnedAsync(urlId, userId, ct))
            {
                return new AnalyticsSummaryDto(0, [], [], []);
            }

            var events = FilteredEvents(urlId, dateFrom, dateTo);

            var totalClicks = await events.CountAsync(ct);

            // The series always starts at daily granularity; callers can override it
            // via GetClicksByPeriodAsync when a coarser groupBy was requested.
            var clicksByPeriod = await QueryClicksByPeriodAsync(
                urlId, dateFrom ?? DateTime.MinValue, dateTo ?? DateTime.MaxValue, "day", ct);

            var topReferrers = await QueryNameCountsAsync(
                events.Where(e => e.Referrer != null && e.Referrer != ""),
                e => e.Referrer!, take: 10, ct);

            var clicksByCountry = await QueryNameCountsAsync(
                events.Where(e => e.Country != null && e.Country != ""),
                e => e.Country!, take: int.MaxValue, ct);

            return new AnalyticsSummaryDto(totalClicks, clicksByPeriod, topReferrers, clicksByCountry);
        }

        public async Task<List<ClicksByPeriodDto>> GetClicksByPeriodAsync(
            Guid urlId, string? userId, DateTime dateFrom, DateTime dateTo, string groupBy, CancellationToken ct = default)
        {
            if (!await IsOwnedAsync(urlId, userId, ct))
            {
                return [];
            }

            return await QueryClicksByPeriodAsync(urlId, dateFrom, dateTo, groupBy, ct);
        }

        public async Task<Dictionary<string, int>> GetClicksByCountryAsync(
            Guid urlId, string? userId, CancellationToken ct = default)
        {
            if (!await IsOwnedAsync(urlId, userId, ct))
            {
                return [];
            }

            var rows = await QueryNameCountsAsync(
                FilteredEvents(urlId, null, null).Where(e => e.Country != null && e.Country != ""),
                e => e.Country!, take: int.MaxValue, ct);

            return rows.ToDictionary(r => r.Name, r => r.Count);
        }

        public async Task<Dictionary<string, int>> GetTopReferrersAsync(
            Guid urlId, string? userId, int top, CancellationToken ct = default)
        {
            if (!await IsOwnedAsync(urlId, userId, ct))
            {
                return [];
            }

            var rows = await QueryNameCountsAsync(
                FilteredEvents(urlId, null, null).Where(e => e.Referrer != null && e.Referrer != ""),
                e => e.Referrer!, take: top, ct);

            return rows.ToDictionary(r => r.Name, r => r.Count);
        }

        // PostgreSQL string equality is case-sensitive, same as C#; matches ShortenerUrlService semantics.
        private Task<bool> IsOwnedAsync(Guid urlId, string? userId, CancellationToken ct) =>
            context.ShortenerUrls.AsNoTracking().AnyAsync(u => u.Id == urlId && u.UserId == userId, ct);

        private IQueryable<ClickEvent> FilteredEvents(Guid urlId, DateTime? dateFrom, DateTime? dateTo)
        {
            var query = context.ClickEvents.AsNoTracking().Where(e => e.ShortenerUrlId == urlId);

            if (dateFrom.HasValue)
            {
                query = query.Where(e => e.ClickedAt >= dateFrom.Value);
            }

            if (dateTo.HasValue)
            {
                query = query.Where(e => e.ClickedAt <= dateTo.Value);
            }

            return query;
        }

        /// <summary>
        /// Groups clicks server-side per day/week/month. Week buckets start on Monday,
        /// month buckets collapse to the first day of the month.
        /// </summary>
        private async Task<List<ClicksByPeriodDto>> QueryClicksByPeriodAsync(
            Guid urlId, DateTime dateFrom, DateTime dateTo, string groupBy, CancellationToken ct)
        {
            var query = context.ClickEvents.AsNoTracking()
                .Where(e => e.ShortenerUrlId == urlId && e.ClickedAt >= dateFrom && e.ClickedAt <= dateTo);

            // Identical { Key, Count } projections share one anonymous type, so the switch yields a single list.
            var grouped = groupBy.ToLowerInvariant() switch
            {
                "week" => await query
                    .GroupBy(e => e.ClickedAt.Date.AddDays(-(((int)e.ClickedAt.DayOfWeek + 6) % 7)))
                    .Select(g => new { g.Key, Count = g.Count() })
                    .OrderBy(x => x.Key)
                    .ToListAsync(ct),

                "month" => await query
                    .GroupBy(e => new DateTime(e.ClickedAt.Year, e.ClickedAt.Month, 1))
                    .Select(g => new { g.Key, Count = g.Count() })
                    .OrderBy(x => x.Key)
                    .ToListAsync(ct),

                // "day" and any unrecognized value fall back to daily buckets.
                _ => await query
                    .GroupBy(e => e.ClickedAt.Date)
                    .Select(g => new { g.Key, Count = g.Count() })
                    .OrderBy(x => x.Key)
                    .ToListAsync(ct),
            };

            var format = groupBy.Equals("month", StringComparison.OrdinalIgnoreCase) ? "yyyy-MM" : "yyyy-MM-dd";

            return grouped
                .Select(x => new ClicksByPeriodDto(x.Key.ToString(format, CultureInfo.InvariantCulture), x.Count))
                .ToList();
        }

        // Group-by-name helper shared by referrer and country breakdowns; caller pre-filters null/empty keys.
        private static async Task<List<NameCountDto>> QueryNameCountsAsync(
            IQueryable<ClickEvent> query,
            System.Linq.Expressions.Expression<Func<ClickEvent, string>> keySelector,
            int take,
            CancellationToken ct)
        {
            var rows = await query
                .GroupBy(keySelector)
                .Select(g => new { Name = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.Name)
                .Take(take)
                .ToListAsync(ct);

            return rows.Select(x => new NameCountDto(x.Name, x.Count)).ToList();
        }
    }
}
