using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace ShortenerUrlApp.WebUI.Services;

/// <summary>
/// Minimal, signature-agnostic JWT payload reader for the browser client.
/// The decoded claims are used only to render the UI identity; the API stays
/// the single source of trust because it validates the token server-side.
/// </summary>
public sealed class JwtPayload
{
    private JwtPayload(DateTime expiresAtUtc, Dictionary<string, string> claims)
    {
        ExpiresAtUtc = expiresAtUtc;
        Claims = claims;
    }

    /// <summary>Token "exp" (Unix seconds); DateTime.MaxValue when the claim is absent.</summary>
    public DateTime ExpiresAtUtc { get; }

    public IReadOnlyDictionary<string, string> Claims { get; }

    public string? DisplayName =>
        Find(ClaimTypes.Name, "name", "unique_name");

    public string? Subject =>
        Find(ClaimTypes.NameIdentifier, "sub");

    public string? Email =>
        Find(ClaimTypes.Email, "email");

    /// <summary>
    /// Decodes the payload segment of a compact JWT without verifying the signature.
    /// Returns null for malformed or non-JWT input.
    /// </summary>
    public static JwtPayload? TryParse(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var segments = token.Split('.');
        if (segments.Length < 2)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(DecodeBase64Url(segments[1]));

            var claims = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                claims[property.Name] = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString() ?? string.Empty
                    : property.Value.ToString();
            }

            return new JwtPayload(ReadExpiry(claims), claims);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException)
        {
            return null;
        }
    }

    private static DateTime ReadExpiry(Dictionary<string, string> claims)
    {
        // "exp" is a NumericDate (seconds since the Unix epoch) sent as a JSON number.
        if (claims.TryGetValue("exp", out var raw)
            && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
        }

        return DateTime.MaxValue;
    }

    private static string DecodeBase64Url(string segment)
    {
        var padded = segment.Replace('-', '+').Replace('_', '/');

        padded = (padded.Length % 4) switch
        {
            2 => padded + "==",
            3 => padded + "=",
            _ => padded
        };

        return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
    }

    private string? Find(params string[] claimTypes) =>
        claimTypes
            .Where(type => Claims.TryGetValue(type, out var value) && !string.IsNullOrEmpty(value))
            .Select(type => Claims[type])
            .FirstOrDefault();
}
