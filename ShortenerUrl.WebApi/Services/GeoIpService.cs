using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Exceptions;
using System.Net;
using System.Net.Sockets;

namespace ShortenerUrlApp.WebApi.Services
{
    /// <summary>
    /// Offline GeoIP resolver backed by a MaxMind GeoLite2/GeoIP2 City mmdb database.
    /// The database is loaded into memory once and reused for every lookup (the reader is
    /// intentionally created once, as initialization is expensive). The service degrades
    /// gracefully: when the database file is missing/corrupt or an IP cannot be resolved,
    /// <see cref="Resolve"/> returns null so analytics rows keep their null Country/City.
    /// </summary>
    public class GeoIpService : IGeoIpService
    {
        private readonly DatabaseReader? _reader;
        private readonly ILogger<GeoIpService> _logger;

        public GeoIpService(IConfiguration configuration, ILogger<GeoIpService> logger)
        {
            _logger = logger;
            _reader = TryLoadReader(configuration["GeoIp:DatabasePath"]);
        }

        /// <inheritdoc />
        public GeoIpLocation? Resolve(string? ipAddress)
        {
            if (_reader is null)
                return null;

            if (string.IsNullOrWhiteSpace(ipAddress) || !IPAddress.TryParse(ipAddress, out var ip))
                return null;

            // Private/reserved ranges are never geo-located; skip the reader entirely.
            if (IsPrivateOrReserved(ip))
                return null;

            try
            {
                var response = _reader.City(ip);

                // An address may be present in the database yet carry no location data.
                var country = response.Country?.IsoCode;
                var city = response.City?.Name;

                return country is null && city is null ? null : new GeoIpLocation(country ?? string.Empty, city);
            }
            catch (AddressNotFoundException)
            {
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "GeoIP lookup failed for {IpAddress}.", ipAddress);
                return null;
            }
        }

        private DatabaseReader? TryLoadReader(string? databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
            {
                _logger.LogInformation("GeoIp:DatabasePath is not configured; GeoIP enrichment is disabled.");
                return null;
            }

            if (!File.Exists(databasePath))
            {
                _logger.LogWarning(
                    "GeoIP database not found at '{DatabasePath}'. ClickEvent rows will keep null Country/City; " +
                    "point 'GeoIp:DatabasePath' at a GeoLite2-City.mmdb file to enable enrichment.",
                    databasePath);
                return null;
            }

            try
            {
                // Held in memory so no file handle pins the file and lookups are thread-safe.
                return new DatabaseReader(new MemoryStream(File.ReadAllBytes(databasePath)));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load GeoIP database from '{DatabasePath}'; GeoIP enrichment is disabled.", databasePath);
                return null;
            }
        }

        private static bool IsPrivateOrReserved(IPAddress ip)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6Multicast || ip.Equals(IPAddress.IPv6Loopback))
                return true;

            // IPv4-mapped IPv6 addresses (::ffff:a.b.c.d) carry IPv4 semantics.
            if (ip.AddressFamily == AddressFamily.InterNetworkV6 && ip.IsIPv4MappedToIPv6)
                ip = ip.MapToIPv4();

            if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                // Unique local addresses (fc00::/7) are never geo-located.
                var v6 = ip.GetAddressBytes();
                return (v6[0] & 0xfe) == 0xfc;
            }

            var bytes = ip.GetAddressBytes();
            return bytes[0] == 0                                        // 0.0.0.0/8
                || bytes[0] == 10                                       // 10.0.0.0/8
                || (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) // 100.64.0.0/10 CGNAT
                || bytes[0] == 127                                      // 127.0.0.0/8
                || (bytes[0] == 169 && bytes[1] == 254)                 // 169.254.0.0/16
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) // 172.16.0.0/12
                || (bytes[0] == 192 && bytes[1] == 168)                 // 192.168.0.0/16
                || bytes[0] >= 224;                                     // multicast + reserved
        }
    }
}
