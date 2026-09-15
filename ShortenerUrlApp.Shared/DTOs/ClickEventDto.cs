namespace ShortenerUrlApp.Shared.DTOs
{
    /// <summary>
    /// Public shape of a single stored click event (one redirect on a shortened URL).
    /// </summary>
    public record ClickEventDto(
        Guid Id,
        DateTime ClickedAt,
        string? IpAddress,
        string? UserAgent,
        string? Country,
        string? City,
        string? Referrer);
}
