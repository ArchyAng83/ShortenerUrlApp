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
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                using var scope = serviceProvider.CreateScope();

                try
                {
                    await SyncAsync(scope.ServiceProvider, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
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
            var geoIp = scopedProvider.GetRequiredService<IGeoIpService>();
            var db = redis.GetDatabase();

            var server = redis.GetServer(redis.GetEndPoints()[0]);
            // IServer.Keys() streams matching keys with SCAN under the hood; IServer.Scan
            // does not exist as a public API in StackExchange.Redis 2.11.
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
                var rangeTask = transaction.ListRangeAsync(key);
                // Queued for MULTI/EXEC: must NOT be awaited before ExecuteAsync,
                // the discard only suppresses CS4014.
                _ = transaction.KeyDeleteAsync(key);

                if (!await transaction.ExecuteAsync())
                {
                    // CONCURRENTWRITE conflict — consume the rangeTask to avoid
                    // abandoning the task, then retry this key on the next tick.
                    _ = await rangeTask;
                    continue;
                }

                var shortCode = key.ToString()[KeyPrefix.Length..];

                // The link was deleted between buffering and the drain; drop its orphaned clicks.
                if (!urlIds.TryGetValue(shortCode, out var urlId))
                    continue;

                // Many clicks in a batch share the same client IP; de-dupe lookups within the drain.
                // The mmdb read is cheap (in-memory binary search), so a per-batch cache is plenty.
                var geoCache = new Dictionary<string, GeoIpLocation?>(StringComparer.Ordinal);

                foreach (var item in await rangeTask)
                {
                    ClickEventMeta? meta = TryParse(item);

                    if (meta is null)
                        continue;

                    GeoIpLocation? location = null;
                    if (meta.IpAddress is not null)
                    {
                        if (!geoCache.TryGetValue(meta.IpAddress, out location))
                        {
                            location = geoIp.Resolve(meta.IpAddress);
                            geoCache.Add(meta.IpAddress, location);
                        }
                    }

                    events.Add(new ClickEvent
                    {
                        Id = Guid.NewGuid(),
                        ShortenerUrlId = urlId,
                        ClickedAt = meta.ClickedAt,
                        IpAddress = meta.IpAddress,
                        UserAgent = meta.UserAgent,
                        Referrer = meta.Referrer,
                        Country = location?.CountryCode,
                        City = location?.CityName
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
            catch (Exception)
            {
                // Malformed or foreign-format entry (e.g. left over from an older deploy) — skip it.
                return null;
            }
        }
    }
}
