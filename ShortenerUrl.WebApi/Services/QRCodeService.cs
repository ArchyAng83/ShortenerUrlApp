using System.Security.Cryptography;
using System.Text;
using QRCoder;
using StackExchange.Redis;

namespace ShortenerUrlApp.WebApi.Services
{
    /// <summary>
    /// Generates PNG QR codes with QRCoder and caches each payload in Redis for a day,
    /// keyed by "qrcode:{sha256(url)}" so arbitrary URL characters never appear in the key.
    /// </summary>
    public class QRCodeService(IConnectionMultiplexer redis) : IQRCodeService
    {
        private readonly IDatabase _cache = redis.GetDatabase();

        // Generated codes encode immutable redirect URLs; a day of cache trades one
        // render per link per day for instant repeated downloads.
        private static readonly TimeSpan CacheTtl = TimeSpan.FromDays(1);

        public async Task<byte[]> GenerateQRCodeAsync(string url, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            // SHA-256 hex keeps the Redis key fixed-size and free of separators the URL may contain.
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
            RedisKey key = $"qrcode:{hash}";

            RedisValue cached = await _cache.StringGetAsync(key);
            byte[]? cachedBytes = (byte[]?)cached;
            if (cachedBytes is { Length: > 0 })
            {
                return cachedBytes;
            }

            // ECC level Q (~30% damage tolerance) suits printed/scanned codes;
            // PngByteQRCode renders without System.Drawing, 20 px per module is crisp.
            using var qrGenerator = new QRCodeGenerator();
            using QRCodeData qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(qrCodeData);
            byte[] png = qrCode.GetGraphic(20);

            // All six arguments spelled out: binds unambiguously to the
            // (key, value, expiry, keepTtl, when, flags) overload — SE.Redis 2.11
            // has several same-shaped StringSetAsync overloads, and a 3-arg call
            // silently binds to a different one (this bit CustomAliasTests before).
            await _cache.StringSetAsync(key, png, CacheTtl, false, When.Always, CommandFlags.None);

            return png;
        }
    }
}
