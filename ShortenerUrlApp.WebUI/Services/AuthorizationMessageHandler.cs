using System.Net;
using System.Net.Http.Headers;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components;

namespace ShortenerUrlApp.WebUI.Services;

/// <summary>
/// Attaches the JWT from localStorage as a Bearer token to every API request and,
/// on 401, logs the session out locally and redirects to the login page.
/// </summary>
public sealed class AuthorizationMessageHandler(
    ILocalStorageService storage,
    NavigationManager navigation,
    JwtAuthStateProvider authState) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var token = await storage.GetItemAsync<string>(JwtAuthStateProvider.TokenKey, ct);

        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var response = await base.SendAsync(request, ct);

        // A failed login attempt must not bounce the user around; only protected calls trigger the redirect.
        if (response.StatusCode == HttpStatusCode.Unauthorized && !IsAuthEndpoint(request.RequestUri))
        {
            await authState.SignOutAsync(ct);
            navigation.NavigateTo("/login");
        }

        return response;
    }

    private static bool IsAuthEndpoint(Uri? uri) =>
        uri?.AbsolutePath.Contains("/api/v1/auth", StringComparison.OrdinalIgnoreCase) == true;
}
