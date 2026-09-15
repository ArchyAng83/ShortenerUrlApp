namespace ShortenerUrlApp.Shared.DTOs
{
    /// <summary>
    /// A named dimension (country, referrer, ...) with its click count.
    /// </summary>
    public record NameCountDto(string Name, int Count);

    /// <summary>
    /// Full analytics snapshot for one shortened URL: total clicks, period series,
    /// top referrers and clicks by country.
    /// </summary>
    public record AnalyticsSummaryDto(
        int TotalClicks,
        List<ClicksByPeriodDto> ClicksByPeriod,
        List<NameCountDto> TopReferrers,
        List<NameCountDto> ClicksByCountry);
}
