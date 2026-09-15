using Microsoft.EntityFrameworkCore;
using ShortenerUrlApp.WebApi.Data;
using ShortenerUrlApp.WebApi.Entities;
using StackExchange.Redis;
using System.Text.Json;

namespace ShortenerUrlApp.WebApi.Services
{
    /// <summary>
    /// Drains the "click-events:{shortCode}" Redis lists from the redirect hot path
    /// into the ClickEvents table once a minute, keeping PostgreSQL writes batched.
    /// </summary>
    public class ClickEventSyncWorker(IServiceProvider serviceProvider) : BackgroundService
    {
        private const string KeyPrefix = "click-events:";

        // Mirrors ShortenerUrlService.ClickEventMeta — same camelCase JSON contract.
        internal sealed record ClickEventMeta(DateTime ClickedAt, string? IpAddress, string? UserAgent, string? Referrer);

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // Sync interval (1 minute), matching ClickSyncWorker.
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

                using var scope = serviceProvider.CreateScope();

                try
                {
                    await SyncAsync(scope.ServiceProvider, stoppingToken);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error ClickEventSync: {ex.Message}");
                }
            }
        }

        private static async Task SyncAsync(IServiceProvider scopedProvider, CancellationToken ct)
        {
            var context = scopedProvider.GetRequiredService<ShortenerUrlDbContext>();
            var redis = scopedProvider.GetRequiredService<IConnectionMultiplexer>();
            var db = redis.GetDatabase();

            var server = redis.GetServer(redis.GetEndPoints()[0]);
            var keys = server.Keys(pattern: $"{KeyPrefix}*").ToList();

            if (keys.Count == 0)
                return;

            // Resolve shortCode -> urlId for all buffered codes with a single query.
            var codes = keys.Select(k => k.ToString()[KeyPrefix.Length..]).ToList();
            var urlIds = await context.ShortenerUrls
                .AsNoTracking()
                .Where(u => codes.Contains(u.ShortCode))
                .ToDictionaryAsync(u => u.ShortCode, u => u.Id, ct);

            var events = new List<ClickEvent>();

            foreach (var key in keys)
            {
                // Read + delete are queued in one MULTI/EXEC transaction,
                // so no click appended mid-drain can get lost.
                ITransaction transaction = db.CreateTransaction();
                Task<RedisValue[]> rangeTask = transaction.ListRangeAsync(key);
                _ = transaction.KeyDeleteAsync(key);

                if (!await transaction.ExecuteAsync())
                    continue; // CONCURRENTWRITE conflict — the next tick will retry this key.

                var shortCode = key.ToString()[KeyPrefix.Length..];

                // The link was deleted between buffering and the drain; drop its orphaned clicks.
                if (!urlIds.TryGetValue(shortCode, out var urlId))
                    continue;

                foreach (var item in await rangeTask)
                {
                    ClickEventMeta? meta = TryParse(item);

                    if (meta is null)
                        continue;

                    events.Add(new ClickEvent
                    {
                        Id = Guid.NewGuid(),
                        ShortenerUrlId = urlId,
                        ClickedAt = meta.ClickedAt,
                        IpAddress = meta.IpAddress,
                        UserAgent = meta.UserAgent,
                        Referrer = meta.Referrer
                        // Country/City stay null until a GeoIP enrichment step fills them.
                    });
                }
            }

            if (events.Count > 0)
            {
                // Bulk insert: one round-trip, EF emits a multi-row INSERT.
                context.ClickEvents.AddRange(events);
                await context.SaveChangesAsync(ct);
            }
        }

        private static ClickEventMeta? TryParse(RedisValue value)
        {
            try
            {
                return JsonSerializer.Deserialize<ClickEventMeta>(value.ToString()!, JsonOptions);
            }
            catch (JsonException)
            {
                // Malformed or foreign-format entry (e.g. left over from an older deploy) — skip it.
                return null;
            }
        }
    }
}
