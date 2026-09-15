using Microsoft.EntityFrameworkCore;
using ShortenerUrlApp.WebApi.Constants;
using ShortenerUrlApp.WebApi.Data;
using ShortenerUrlApp.WebApi.Entities;
using StackExchange.Redis;
using System.Security.Cryptography;
using System.Text;

namespace ShortenerUrlApp.WebApi.Services
{
    public class ShortenerUrlService(ShortenerUrlDbContext context, IConnectionMultiplexer redis) : IShortenerUrlService
    {
        //Redis для обработки 10к кликов в секунду
        private readonly IDatabase _cache = redis.GetDatabase();

        public async Task<bool> DeleteUrlAsync(Guid id, string? userId, CancellationToken ct = default)
        {
            var shortenerUrl = await context.ShortenerUrls
                .FirstOrDefaultAsync(u => u.Id == id, ct);

            // Treat foreign URLs as "not found" so the API does not leak their existence.
            if (shortenerUrl is null || shortenerUrl.UserId != userId)
            {
                return false;
            }

            await _cache.KeyDeleteAsync($"url:{shortenerUrl.ShortCode}");
            await _cache.KeyDeleteAsync($"clicks:{shortenerUrl.ShortCode}");

            context.ShortenerUrls.Remove(shortenerUrl);
            await context.SaveChangesAsync(ct);

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
            // Public redirect path: no ownership check, the short code is the capability.
            string? cachedUrl = await _cache.StringGetAsync($"url:{shortCode}");

            if (!string.IsNullOrEmpty(cachedUrl))
            { 
                // This does not block the PostgreSQL database at 10k requests per second.
                _ = _cache.StringIncrementAsync($"clicks:{shortCode}");
                return cachedUrl;
            }


            var shortenerUrl = await context.ShortenerUrls
                .FirstOrDefaultAsync(u => u.ShortCode == shortCode, ct); ;

            if (shortenerUrl is null)
                return null!;

            await _cache.StringSetAsync($"url:{shortCode}", shortenerUrl.LongUrl, TimeSpan.FromDays(1));

            _ = await _cache.StringIncrementAsync($"clicks:{shortCode}");

            return shortenerUrl.LongUrl;
        }

        public async Task<string> ShortenUrlAsync(string longUrl, string? userId, CancellationToken ct = default)
        {
            string code;

            do
            {
                code = GenerateCode();
            }
            while (await context.ShortenerUrls.AnyAsync(u => u.ShortCode == code, ct));

            var shotenerUrl = new ShortenerUrl()
            {
                LongUrl = longUrl,
                ShortCode = code,
                UserId = userId
            };

            context.ShortenerUrls.Add(shotenerUrl);
            await context.SaveChangesAsync(ct);

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

        //Для синхронизации Redis с БД
        public async Task SyncClicksToDbAsync(CancellationToken ct)
        {
            var server = redis.GetServer(redis.GetEndPoints()[0]);
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
