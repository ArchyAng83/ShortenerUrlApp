namespace ShortenerUrlApp.WebApi.Services
{
    /// <summary>
    /// Resolves geo metadata (country ISO code + city name) for a client IP address.
    /// </summary>
    public interface IGeoIpService
    {
        /// <summary>
        /// Returns the geo location for <paramref name="ipAddress"/>, or null when the IP is
        /// missing, malformed, private/reserved, unmapped in the database, or the database is unavailable.
        /// </summary>
        GeoIpLocation? Resolve(string? ipAddress);
    }

    /// <summary>
    /// Geo location resolved for an IP address by <see cref="IGeoIpService"/>.
    /// </summary>
    public sealed record GeoIpLocation(string CountryCode, string? CityName);
}
