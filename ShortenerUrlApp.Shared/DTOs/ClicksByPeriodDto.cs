namespace ShortenerUrlApp.Shared.DTOs
{
    /// <summary>
    /// Click count aggregated into one period bucket.
    /// Date is a stable invariant-culture string: "yyyy-MM-dd" for day/week buckets, "yyyy-MM" for month buckets.
    /// </summary>
    public record ClicksByPeriodDto(string Date, int Count);
}
