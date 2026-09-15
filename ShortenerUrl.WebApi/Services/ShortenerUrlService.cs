using Microsoft.EntityFrameworkCore;
using ShortenerUrlApp.WebApi.Constants;
using ShortenerUrlApp.WebApi.Data;
using ShortenerUrlApp.WebApi.Entities;
using StackExchange.Redis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ShortenerUrlApp.WebApi.Services
{
    // The IHttpContextAccessor parameter is optional so pre-existing construction sites
    // (and unit tests) keep compiling; DI always supplies it at runtime.
    public class ShortenerUrlService(
        ShortenerUrlDbContext context,
        IConnectionMultiplexer redis,
        IHttpContextAccessor? httpContextAccessor = null) : IShortenerUrlService
    {
        //Redis для обработки 10к кликов в секунду
        private readonly IDatabase _cache = redis.GetDatabase();

        // Shape of the click-event JSON pushed to the "click-events:{shortCode}" Redis lists.
        // Field names are camelCased to match the analytics ingestion contract (ClickEventSyncWorker).
        private sealed record ClickEventMeta(DateTime ClickedAt, string? IpAddress, string? UserAgent, string? Referrer);

        private static readonly JsonSerializerOptions ClickJsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public async Task<bool> DeleteUrlAsync(Guid id, string? userId, CancellationToken ct = default)
        {
            var shortenerUrl = await context.ShortenerUrls
                .FirstOrDefaultAsync(u => u.Id == id, ct);

            // Treat foreign URLs as "not found" so the API does not leak their existence.
            if (shortenerUrl is null || shortenerUrl.UserId != userId)
            {
                return false;
            }

            context.ShortenerUrls.Remove(shortenerUrl);
            await context.SaveChangesAsync(ct);

            // Evict Redis keys after DB deletion to avoid inconsistency if SaveChangesAsync fails.
            // Cache deletion failures are non-critical; the DB is the source of truth.
            try
            {
                await _cache.KeyDeleteAsync($"url:{shortenerUrl.ShortCode}");
                await _cache.KeyDeleteAsync($"clicks:{shortenerUrl.ShortCode}");
                await _cache.KeyDeleteAsync($"click-events:{shortenerUrl.ShortCode}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Cache eviction failed for {shortenerUrl.ShortCode}: {ex.Message}");
            }

            return true;
        }

        public async Task<List<ShortenerUrl>> GetAllUrlsAsync(string? userId, CancellationToken ct = default) =>
            await context.ShortenerUrls
                            .AsNoTracking()
                            .Where(u => u.UserId == userId) // PostgreSQL string equality is case-sensitive, same as C#
                            .OrderByDescending(u => u.CreateAt)
                            .ToListAsync(ct);

        public async Task<string> GetLongUrlAsync(string shortCode, CancellationToken ct = default)
        {
            // Compatibility wrapper for callers that only need the URL (null when unresolvable).
            var result = await GetLongUrlWithStatusAsync(shortCode, ct);
            return result.LongUrl!;
        }

        public async Task<RedirectResult> GetLongUrlWithStatusAsync(string shortCode, CancellationToken ct = default)
        {
            // Public redirect path: no ownership check, the short code is the capability.
            string? cachedUrl = await _cache.StringGetAsync($"url:{shortCode}");

            if (!string.IsNullOrEmpty(cachedUrl))
            {
                // This does not block the PostgreSQL database at 10k requests per second.
                // Links with a click cap are never cached (a cached snapshot cannot track live
                // click counts), and a cached entry's TTL ends no later than ExpiresAt,
                // so a cache hit is always safe to redirect.
                _ = _cache.StringIncrementAsync($"clicks:{shortCode}")
                    .ContinueWith(t => { if (t.IsFaulted) Console.WriteLine($"Click increment failed: {t.Exception?.InnerException?.Message}"); });
                QueueClickEvent(shortCode);
                return RedirectResult.Redirect(cachedUrl);
            }

            var shortenerUrl = await context.ShortenerUrls
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.ShortCode == shortCode, ct);

            if (shortenerUrl is null)
                return RedirectResult.NotFound();

            var now = DateTime.UtcNow;

            if (shortenerUrl.ExpiresAt.HasValue && shortenerUrl.ExpiresAt < now)
                return RedirectResult.Expired();

            // Counted clicks are flushed to PostgreSQL only every minute, so the pending
            // Redis counter must be included for the MaxClicks cap to be accurate.
            if (shortenerUrl.MaxClicks.HasValue)
            {
                RedisValue pendingClicks = await _cache.StringGetAsync($"clicks:{shortCode}");
                long totalClicks = shortenerUrl.CountOfClick +
                                   (pendingClicks.HasValue ? (long)pendingClicks : 0);

                if (totalClicks >= shortenerUrl.MaxClicks.Value)
                    return RedirectResult.LimitReached();
            }

            _ = await _cache.StringIncrementAsync($"clicks:{shortCode}");

            // Cache only uncapped links; the TTL is clamped so the entry dies at ExpiresAt.
            if (!shortenerUrl.MaxClicks.HasValue)
            {
                var ttl = TimeSpan.FromDays(1);

                if (shortenerUrl.ExpiresAt.HasValue)
                {
                    var remaining = shortenerUrl.ExpiresAt.Value - now;
                    if (remaining < ttl)
                        ttl = remaining;
                }

                if (ttl > TimeSpan.Zero)
                    await _cache.StringSetAsync($"url:{shortCode}", shortenerUrl.LongUrl, ttl);
            }

            // Count a click only when the redirect actually happens (expired/capped links above exit early).
            QueueClickEvent(shortCode);

            return RedirectResult.Redirect(shortenerUrl.LongUrl);
        }

        // Captures click metadata as JSON into the "click-events:{shortCode}" Redis list.
        // Fire-and-forget on purpose: analytics logging must never slow down or fail the redirect.
        private async void QueueClickEvent(string shortCode)
        {
            try
            {
                HttpContext? httpContext = httpContextAccessor?.HttpContext;
                HttpRequest? request = httpContext?.Request;

                var meta = new ClickEventMeta(
                    DateTime.UtcNow,
                    httpContext?.Connection.RemoteIpAddress?.ToString(),
                    Nullify(request?.Headers.UserAgent.ToString()),
                    // Note the HTTP "Referer" header spelling.
                    Nullify(request?.Headers.Referer.ToString()));

                string json = JsonSerializer.Serialize(meta, ClickJsonOptions);

                await _cache.ListLeftPushAsync($"click-events:{shortCode}", json);
            }
            catch (Exception ex)
            {
                // Analytics must never fail the redirect.
                Console.WriteLine($"QueueClickEvent failed: {ex.Message}");
            }
        }

        private static string? Nullify(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

        // Legacy overload: random code, no alias, no expiry, no click cap.
        public Task<string> ShortenUrlAsync(string longUrl, string? userId, CancellationToken ct) =>
            ShortenUrlAsync(longUrl, userId, null, null, null, ct);

        public async Task<string> ShortenUrlAsync(
            string longUrl,
            string? userId,
            string? customAlias = null,
            int? expiresInMinutes = null,
            int? maxClicks = null,
            CancellationToken ct = default)
        {
            string code;
            var isCustomAlias = !string.IsNullOrWhiteSpace(customAlias);

            if (isCustomAlias)
            {
                code = customAlias!.Trim();

                if (await context.ShortenerUrls.AnyAsync(u => u.ShortCode == code, ct))
                {
                    throw new AliasAlreadyInUseException(code);
                }
            }
            else
            {
                do
                {
                    code = GenerateCode();
                }
                while (await context.ShortenerUrls.AnyAsync(u => u.ShortCode == code, ct));
            }

            var shotenerUrl = new ShortenerUrl()
            {
                LongUrl = longUrl,
                ShortCode = code,
                UserId = userId,
                IsCustomAlias = isCustomAlias,
                ExpiresAt = expiresInMinutes.HasValue
                    ? DateTime.UtcNow.AddMinutes(expiresInMinutes.Value)
                    : null,
                MaxClicks = maxClicks
            };

            context.ShortenerUrls.Add(shotenerUrl);

            try
            {
                await context.SaveChangesAsync(ct);
            }
            catch (DbUpdateException) when (isCustomAlias)
            {
                // Lost the race for the same alias to another request; the unique
                // index on ShortCode is the source of truth here.
                throw new AliasAlreadyInUseException(code);
            }
            catch (DbUpdateException)
            {
                // Lost the race for a randomly generated code to another request.
                // Detach the failed entity and retry with a new code.
                context.Entry(shotenerUrl).State = EntityState.Detached;

                // Retry up to a few times to avoid infinite loops on persistent collisions.
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    code = GenerateCode();
                    if (!await context.ShortenerUrls.AnyAsync(u => u.ShortCode == code, ct))
                    {
                        shotenerUrl.ShortCode = code;
                        context.ShortenerUrls.Add(shotenerUrl);
                        try
                        {
                            await context.SaveChangesAsync(ct);
                            return code;
                        }
                        catch (DbUpdateException)
                        {
                            context.Entry(shotenerUrl).State = EntityState.Detached;
                            continue;
                        }
                    }
                }

                throw new AliasAlreadyInUseException(code);
            }

            return code;
        }

        public async Task<bool> UpdateUrlAsync(Guid id, string newLongUrl, string? userId, CancellationToken ct = default)
        {
            var shortenerUrl = await context.ShortenerUrls
                .FirstOrDefaultAsync(u => u.Id == id, ct);

            // Treat foreign URLs as "not found" so the API does not leak their existence.
            if (shortenerUrl is null || shortenerUrl.UserId != userId)
            {
                return false;
            }

            shortenerUrl.LongUrl = newLongUrl;
            await context.SaveChangesAsync(ct);

            await _cache.KeyDeleteAsync($"url:{shortenerUrl.ShortCode}");

            return true;
        }

        public async Task<int> DeleteExpiredUrlsAsync(CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;

            var expired = await context.ShortenerUrls
                .Where(u => u.ExpiresAt.HasValue && u.ExpiresAt < now)
                .ToListAsync(ct);

            if (expired.Count == 0)
                return 0;

            foreach (var url in expired)
            {
                await _cache.KeyDeleteAsync($"url:{url.ShortCode}");
                await _cache.KeyDeleteAsync($"clicks:{url.ShortCode}");
                await _cache.KeyDeleteAsync($"click-events:{url.ShortCode}");
            }

            context.ShortenerUrls.RemoveRange(expired);
            await context.SaveChangesAsync(ct);

            return expired.Count;
        }

        //Для синхронизации Redis с БД
        public async Task SyncClicksToDbAsync(CancellationToken ct)
        {
            var server = redis.GetServer(redis.GetEndPoints()[0]);
            // IServer.Keys() streams matching keys with SCAN under the hood; IServer.Scan
            // does not exist as a public API in StackExchange.Redis 2.11.
            var keys = server.Keys(pattern: "clicks:*").ToList();

            foreach (var key in keys)
            {
                var shortCode = key.ToString().Replace("clicks:", "");

                // Получаем значение и удаляем ключ из Redis за одну операцию
                var clicksValue = await _cache.StringGetDeleteAsync(key);

                if (clicksValue.HasValue && (int)clicksValue > 0)
                {
                    int clicks = (int)clicksValue;

                    //Выполняем быстрый UPDATE в базе без загрузки всей сущности в память
                    await context.ShortenerUrls
                        .Where(u => u.ShortCode == shortCode)
                        .ExecuteUpdateAsync(s => s.SetProperty(
                            u => u.CountOfClick,
                            u => u.CountOfClick + clicks),
                            ct);
                }
            }
        }

        //public, чтобы тесты прошли
        public static string GenerateCode()
        {
            var sb = new StringBuilder(Constant.MAX_LENGTH_SHORT_URL);

            for (int i = 0; i < Constant.MAX_LENGTH_SHORT_URL; i++)
            {
                // Математически стойкий выбор индекса без смещения 
                int index = RandomNumberGenerator.GetInt32(Constant.ALPHABET.Length);
                sb.Append(Constant.ALPHABET[index]);
            }

            return sb.ToString();
        }
    }
}
