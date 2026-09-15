namespace ShortenerUrlApp.WebUI.Services;

/// <summary>
/// Small helpers for working with the absolute short URLs returned by the API
/// (e.g. "http://localhost:5153/myalias").
/// </summary>
public static class UrlHelpers
{
    /// <summary>Extracts the short code segment ("myalias") from a full short URL.</summary>
    public static string CodeFromShortUrl(string shortUrl)
    {
        var lastSlash = shortUrl.LastIndexOf('/');

        return lastSlash >= 0 ? shortUrl[(lastSlash + 1)..] : shortUrl;
    }
}
