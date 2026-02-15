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
        private readonly IDatabase _cache = redis.GetDatabase();

        public async Task DeleteUrlAsync(Guid id, CancellationToken ct = default)
        {
            var shortenerUrl = await context.ShortenerUrls.FindAsync(id, ct);

            if (shortenerUrl is not null)
            {
                await _cache.KeyDeleteAsync($"url:{shortenerUrl.ShortCode}");
                await _cache.KeyDeleteAsync($"clicks:{shortenerUrl.ShortCode}");

                context.ShortenerUrls.Remove(shortenerUrl);
                await context.SaveChangesAsync(ct);
            }
        }

        public async Task<List<ShortenerUrl>> GetAllUrlsAsync(CancellationToken ct = default) =>
            await context.ShortenerUrls
                            .AsNoTracking()
                            .OrderByDescending(u => u.CreateAt)
                            .ToListAsync(ct);

        public async Task<string> GetLongUrlAsync(string shortCode, CancellationToken ct = default)
        {
            string? cachedUrl = await _cache.StringGetAsync($"url:{shortCode}");

            if (!string.IsNullOrEmpty(cachedUrl))
            { 
                // Это не блокирует базу данных MySQL при 10к запросах в секунду.
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

        public async Task<string> ShortenUrlAsync(string longUrl, CancellationToken ct = default)
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
                ShortCode = code
            };

            context.ShortenerUrls.Add(shotenerUrl);
            await context.SaveChangesAsync(ct);

            return code;
        }

        public async Task UpdateUrlAsync(Guid id, string newLongUrl, CancellationToken ct = default)
        {
            var shortenerUrl = await context.ShortenerUrls.FindAsync(id, ct);

            if (shortenerUrl is not null)
            {
                shortenerUrl.LongUrl = newLongUrl;
                await context.SaveChangesAsync(ct);

                await _cache.KeyDeleteAsync($"url:{shortenerUrl.ShortCode}");
            }
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

        private static string GenerateCode()
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
