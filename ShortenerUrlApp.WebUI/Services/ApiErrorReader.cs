using System.Net.Http.Json;
using System.Text.Json;

namespace ShortenerUrlApp.WebUI.Services;

/// <summary>
/// Normalizes the different error payloads the API can return into plain strings:
/// a JSON array of messages (auth failures), a ProblemDetails/ValidationProblemDetails,
/// a bare quoted string, or the raw body as a last resort.
/// </summary>
public static class ApiErrorReader
{
    public static async Task<IReadOnlyList<string>> ReadErrorsAsync(HttpResponseMessage response, CancellationToken ct = default)
    {
        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(ct);
        }
        catch (HttpRequestException)
        {
            return [$"Request failed ({(int)response.StatusCode} {response.StatusCode})."];
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return [$"Request failed ({(int)response.StatusCode} {response.StatusCode})."];
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            // AuthController returns BadRequest(result.Errors): a flat array of strings.
            if (root.ValueKind == JsonValueKind.Array)
            {
                var messages = root.EnumerateArray()
                    .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString())
                    .Where(m => !string.IsNullOrWhiteSpace(m))
                    .ToList();

                if (messages.Count > 0)
                {
                    return messages!;
                }
            }

            // FluentValidation / ModelState pipeline: ProblemDetails with an "errors" dictionary.
            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                return errors.EnumerateObject()
                    .SelectMany(field => field.Value.ValueKind == JsonValueKind.Array
                        ? field.Value.EnumerateArray().Select(v => $"{field.Name}: {v}")
                        : [$"{field.Name}: {field.Value}"])
                    .ToList();
            }

            if (root.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
            {
                return [detail.GetString()!];
            }
        }
        catch (JsonException)
        {
            // Not JSON — fall through to the raw text below.
        }

        return [body.Trim('"')];
    }
}
