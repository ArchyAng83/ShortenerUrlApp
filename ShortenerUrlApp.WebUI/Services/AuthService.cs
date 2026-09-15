using System.Net.Http.Json;
using ShortenerUrlApp.Shared.DTOs;

namespace ShortenerUrlApp.WebUI.Services;

/// <summary>
/// Outcome of an auth API call: the token bundle on success, human-readable errors on failure.
/// </summary>
public sealed record AuthApiResult(bool Succeeded, AuthResponseDto? Auth, IReadOnlyList<string> Errors);

/// <summary>
/// Thin client wrapper around the AuthController endpoints of the Web API.
/// </summary>
public sealed class AuthService(HttpClient http)
{
    public Task<AuthApiResult> LoginAsync(LoginDto dto, CancellationToken ct = default) =>
        PostAsync("api/v1/auth/login", dto, ct);

    public Task<AuthApiResult> RegisterAsync(RegisterDto dto, CancellationToken ct = default) =>
        PostAsync("api/v1/auth/register", dto, ct);

    private async Task<AuthApiResult> PostAsync(string endpoint, object dto, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsJsonAsync(endpoint, dto, ct);
        }
        catch (HttpRequestException)
        {
            return new AuthApiResult(false, null, ["The server could not be reached. Is the API running?"]);
        }

        if (response.IsSuccessStatusCode)
        {
            var auth = await response.Content.ReadFromJsonAsync<AuthResponseDto>(cancellationToken: ct);

            return auth is null
                ? new AuthApiResult(false, null, ["Unexpected empty response from the server."])
                : new AuthApiResult(true, auth, []);
        }

        return new AuthApiResult(false, null, await ApiErrorReader.ReadErrorsAsync(response, ct));
    }
}
