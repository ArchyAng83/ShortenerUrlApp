using System.Security.Claims;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;
using ShortenerUrlApp.Shared.DTOs;

namespace ShortenerUrlApp.WebUI.Services;

/// <summary>
/// Authentication state for this standalone Blazor WASM client. The API itself issues
/// and validates JWTs (HS256); here the token keeps a copy in localStorage and is decoded
/// locally by <see cref="JwtPayload"/> to build the <see cref="ClaimsPrincipal"/> that
/// drives [Authorize] and &lt;AuthorizeView&gt;. Security always stays on the server's side —
/// client-side claims are presentation only, and every protected API call is re-checked.
/// </summary>
public sealed class JwtAuthStateProvider(
    ILocalStorageService storage,
    ILogger<JwtAuthStateProvider> logger) : AuthenticationStateProvider
{
    /// <summary>localStorage key holding the JWT (referenced by AuthorizationMessageHandler too).</summary>
    public const string TokenKey = "authToken";

    private static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));

    /// <summary>
    /// Persists a fresh token from the auth endpoint and notifies the UI to log in.
    /// </summary>
    public async Task SignInAsync(AuthResponseDto auth, CancellationToken ct = default)
    {
        await storage.SetItemAsync(TokenKey, auth.Token, ct);

        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    /// <summary>
    /// Drops the stored token and notifies the UI to log out.
    /// </summary>
    public async Task SignOutAsync(CancellationToken ct = default)
    {
        await storage.RemoveItemAsync(TokenKey, ct);

        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            var token = await storage.GetItemAsync<string>(TokenKey);

            var payload = JwtPayload.TryParse(token);

            if (payload is null)
            {
                return Anonymous;
            }

            if (payload.ExpiresAtUtc <= DateTime.UtcNow)
            {
                // Stale token: clean up so the handler stops attaching it to requests.
                await storage.RemoveItemAsync(TokenKey);
                return Anonymous;
            }

            return new AuthenticationState(BuildPrincipal(payload, token!));
        }
        catch (Exception ex) when (ex is Microsoft.JSInterop.JSException or InvalidOperationException)
        {
            // localStorage can be unavailable (private mode, prerender race); stay anonymous.
            logger.LogWarning(ex, "Failed to read authentication state from localStorage.");
            return Anonymous;
        }
    }

    private static ClaimsPrincipal BuildPrincipal(JwtPayload payload, string token)
    {
        var claims = new List<Claim>();

        AddIfPresent(claims, ClaimTypes.NameIdentifier, payload.Subject);
        AddIfPresent(claims, ClaimTypes.Name, payload.DisplayName);
        AddIfPresent(claims, ClaimTypes.Email, payload.Email);

        // Expiry kept for display/debug purposes only; enforcement happens on the API.
        claims.Add(new Claim("token_expires_at", payload.ExpiresAtUtc.ToString("O")));

        var identity = new ClaimsIdentity(claims, authenticationType: "jwt", nameType: ClaimTypes.Name, roleType: ClaimTypes.Role);

        return new ClaimsPrincipal(identity);
    }

    private static void AddIfPresent(List<Claim> claims, string type, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            claims.Add(new Claim(type, value));
        }
    }
}
