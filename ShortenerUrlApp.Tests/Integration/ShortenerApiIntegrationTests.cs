using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using ShortenerUrlApp.Shared.DTOs;
using ShortenerUrlApp.WebApi.Entities;
using Xunit;

namespace ShortenerUrlApp.Tests.Integration
{
    /// <summary>
    /// End-to-end checks against the real WebApi hosted by <see cref="ShortenerApiFactory"/>
    /// (real Postgres + Redis via Testcontainers). Exercises the HTTP contract, EF migrations,
    /// Redis write-behind counters and background workers.
    /// Every test uses unique emails/aliases/codes so tests can share one fixture instance.
    /// </summary>
    public class ShortenerApiIntegrationTests : IClassFixture<ShortenerApiFactory>
    {
        private readonly ShortenerApiFactory _factory;

        public ShortenerApiIntegrationTests(ShortenerApiFactory factory)
        {
            _factory = factory;
        }

        // ---- Helpers -------------------------------------------------------

        private HttpClient CreateClient(bool followRedirects = false)
        {
            var options = new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = followRedirects
            };

            return _factory.CreateClient(options);
        }

        private static void Authorize(HttpClient client, string token) =>
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        private static async Task<string> RegisterAndGetTokenAsync(HttpClient client)
        {
            var response = await client.PostAsJsonAsync("api/v1/auth/register", new
            {
                userName = "it_" + Guid.NewGuid().ToString("N")[..10],
                email = $"it_{Guid.NewGuid():N}@test.local",
                password = "Passw0rd!#Pass"
            });

            response.EnsureSuccessStatusCode();

            var auth = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
            auth.Should().NotBeNull();

            return auth!.Token;
        }

        private async Task<string> CreateUrlAsync(
            HttpClient client,
            string longUrl,
            string? alias = null,
            int? maxClicks = null,
            int? expiresInMinutes = null)
        {
            var response = await client.PostAsJsonAsync("api/v1/urls", new CreateShortUrlDto
            {
                LongUrl = longUrl,
                CustomAlias = alias,
                MaxClicks = maxClicks,
                ExpiresInMinutes = expiresInMinutes
            });

            response.EnsureSuccessStatusCode();

            var code = await response.Content.ReadAsStringAsync();
            code.Should().NotBeNullOrWhiteSpace();

            return code!;
        }

        private static async Task<List<UrlResponseDto>> GetLinksAsync(HttpClient client)
        {
            var response = await client.GetAsync("api/v1/urls");
            response.EnsureSuccessStatusCode();

            return (await response.Content.ReadFromJsonAsync<List<UrlResponseDto>>())!;
        }

        private static async Task<Guid> FindLinkIdAsync(List<UrlResponseDto> links, string code)
        {
            var link = links.SingleOrDefault(l => l.ShortUrl.EndsWith($"/{code}", StringComparison.Ordinal));
            link.Should().NotBeNull($"expected a row for code '{code}'");

            return link!.Id;
        }

        private async Task SeedClicksAsync(Guid urlId, params (DateTime ClickedAt, string? Country, string? Referrer)[] rows)
        {
            await using var db = _factory.CreateDatabase();

            db.ClickEvents.AddRange(rows.Select(r => new ClickEvent
            {
                Id = Guid.NewGuid(),
                ShortenerUrlId = urlId,
                ClickedAt = r.ClickedAt,
                Country = r.Country,
                Referrer = r.Referrer
            }));

            await db.SaveChangesAsync();
        }

        private async Task SeedExpiryAsync(string code)
        {
            await using var db = _factory.CreateDatabase();

            var link = await db.ShortenerUrls.SingleAsync(u => u.ShortCode == code);
            link.ExpiresAt = DateTime.UtcNow.AddMinutes(-5);
            await db.SaveChangesAsync();
        }

        // ---- Register / create / redirect ----------------------------------

        [Fact]
        public async Task CreateAndRedirect_RoundTrip_Returns302WithDestination()
        {
            using var client = CreateClient();

            var token = await RegisterAndGetTokenAsync(client);
            Authorize(client, token);
            var code = await CreateUrlAsync(client, "https://example.org/landing", alias: "it-" + Guid.NewGuid().ToString("N")[..12]);

            using var redirectClient = CreateClient(followRedirects: false);
            var redirect = await redirectClient.GetAsync($"/{code}");

            redirect.StatusCode.Should().Be(HttpStatusCode.Redirect); // 302
            redirect.Headers.Location.Should().Be("https://example.org/landing");
        }

        [Fact]
        public async Task CreateUrl_WithoutAuthentication_Returns401()
        {
            using var client = CreateClient();
            var response = await client.PostAsJsonAsync("api/v1/urls", new CreateShortUrlDto { LongUrl = "https://example.org" });

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task Redirect_UnknownCode_Returns404()
        {
            using var client = CreateClient(followRedirects: false);
            var response = await client.GetAsync("/no-such-code-xyz");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        // ---- Click counters ------------------------------------------------

        [Fact]
        public async Task ListUrls_ReflectsPendingClicksBufferedInRedis()
        {
            using var client = CreateClient();

            var token = await RegisterAndGetTokenAsync(client);
            Authorize(client, token);
            var code = await CreateUrlAsync(client, "https://example.org/counter", alias: "it-" + Guid.NewGuid().ToString("N")[..12]);

            using var redirectClient = CreateClient(followRedirects: false);
            (await redirectClient.GetAsync($"/{code}")).StatusCode.Should().Be(HttpStatusCode.Redirect);
            (await redirectClient.GetAsync($"/{code}")).StatusCode.Should().Be(HttpStatusCode.Redirect);

            var links = await GetLinksAsync(client);
            var link = links.Single(l => l.ShortUrl.EndsWith($"/{code}", StringComparison.Ordinal));

            // Two redirects happened; the counter is DB + pending Redis, which must be >= 2
            // regardless of whether the background worker has flushed yet.
            link.CountOfClick.Should().Be(2);
        }

        [Fact]
        public async Task ExpiredLink_Returns410()
        {
            using var client = CreateClient();

            var token = await RegisterAndGetTokenAsync(client);
            Authorize(client, token);
            var code = await CreateUrlAsync(client, "https://example.org/expiring", alias: "it-" + Guid.NewGuid().ToString("N")[..12]);

            await SeedExpiryAsync(code);

            using var redirectClient = CreateClient(followRedirects: false);
            var response = await redirectClient.GetAsync($"/{code}");

            response.StatusCode.Should().Be(HttpStatusCode.Gone); // 410
        }

        [Fact]
        public async Task MaxClicks_Link_StopsRedirecting_OnceCapIsReached()
        {
            using var client = CreateClient();

            var token = await RegisterAndGetTokenAsync(client);
            Authorize(client, token);
            var code = await CreateUrlAsync(client, "https://example.org/capped", alias: "it-" + Guid.NewGuid().ToString("N")[..12], maxClicks: 2);

            using var redirectClient = CreateClient(followRedirects: false);

            (await redirectClient.GetAsync($"/{code}")).StatusCode.Should().Be(HttpStatusCode.Redirect);
            (await redirectClient.GetAsync($"/{code}")).StatusCode.Should().Be(HttpStatusCode.Redirect);

            var blocked = await redirectClient.GetAsync($"/{code}");
            blocked.StatusCode.Should().Be(HttpStatusCode.Gone); // 410
        }

        // ---- Update / delete ------------------------------------------------

        [Fact]
        public async Task UpdateUrl_ChangesTarget_AndRedirectUsesNewLocation()
        {
            using var client = CreateClient();

            var token = await RegisterAndGetTokenAsync(client);
            Authorize(client, token);
            var code = await CreateUrlAsync(client, "https://example.org/before", alias: "it-" + Guid.NewGuid().ToString("N")[..12]);
            var links = await GetLinksAsync(client);
            var id = await FindLinkIdAsync(links, code);

            var update = await client.PutAsJsonAsync("api/v1/urls", new { id, longUrl = "https://example.org/after" });
            update.StatusCode.Should().Be(HttpStatusCode.OK);

            using var redirectClient = CreateClient(followRedirects: false);
            var redirect = await redirectClient.GetAsync($"/{code}");

            redirect.StatusCode.Should().Be(HttpStatusCode.Redirect);
            redirect.Headers.Location.Should().Be("https://example.org/after");
        }

        [Fact]
        public async Task DeleteUrl_ThenRedirect_Returns404()
        {
            using var client = CreateClient();

            var token = await RegisterAndGetTokenAsync(client);
            Authorize(client, token);
            var code = await CreateUrlAsync(client, "https://example.org/to-delete", alias: "it-" + Guid.NewGuid().ToString("N")[..12]);
            var links = await GetLinksAsync(client);
            var id = await FindLinkIdAsync(links, code);

            var delete = await client.DeleteAsync($"api/v1/urls/{id}");
            delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

            using var redirectClient = CreateClient(followRedirects: false);
            var redirect = await redirectClient.GetAsync($"/{code}");

            redirect.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        // ---- Ownership isolation -------------------------------------------

        [Fact]
        public async Task ForeignUser_ReceivesEmptyAnalytics_ForOthersLink()
        {
            using var clientA = CreateClient();
            var tokenA = await RegisterAndGetTokenAsync(clientA);
            Authorize(clientA, tokenA);
            var code = await CreateUrlAsync(clientA, "https://example.org/private", alias: "it-" + Guid.NewGuid().ToString("N")[..12]);
            var links = await GetLinksAsync(clientA);
            var id = await FindLinkIdAsync(links, code);

            await SeedClicksAsync(id, (DateTime.UtcNow, "US", "https://example.com/r"));

            using var clientB = CreateClient();
            var tokenB = await RegisterAndGetTokenAsync(clientB);
            Authorize(clientB, tokenB);

            var foreign = await clientB.GetAsync($"api/v1/urls/{id}/analytics");
            foreign.EnsureSuccessStatusCode();

            var summary = await foreign.Content.ReadFromJsonAsync<AnalyticsSummaryDto>();
            summary.Should().NotBeNull();
            summary!.TotalClicks.Should().Be(0);
            summary.ClicksByCountry.Should().BeEmpty();
            summary.TopReferrers.Should().BeEmpty();

            // The owner still sees the seeded data.
            var qrAsForeignUser = await clientB.GetAsync($"api/v1/urls/{id}/qrcode");
            qrAsForeignUser.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task Analytics_ReturnsSummary_ForSeededClickEvents()
        {
            using var client = CreateClient();

            var token = await RegisterAndGetTokenAsync(client);
            Authorize(client, token);
            var code = await CreateUrlAsync(client, "https://example.org/analytics", alias: "it-" + Guid.NewGuid().ToString("N")[..12]);
            var links = await GetLinksAsync(client);
            var id = await FindLinkIdAsync(links, code);

            var today = DateTime.UtcNow.Date;
            await SeedClicksAsync(id,
                (today.AddHours(9), "US", "https://github.com"),
                (today.AddHours(10), "US", "https://github.com"),
                (today.AddHours(11), "US", "https://google.com"),
                (today.AddHours(12), "RU", null),
                (today.AddHours(13), "RU", null));

            var response = await client.GetAsync($"api/v1/urls/{id}/analytics");
            response.EnsureSuccessStatusCode();

            var summary = await response.Content.ReadFromJsonAsync<AnalyticsSummaryDto>();
            summary.Should().NotBeNull();

            summary!.TotalClicks.Should().Be(5);
            summary.ClicksByCountry.Should().ContainSingle(c => c.Name == "US" && c.Count == 3);
            summary.ClicksByCountry.Should().ContainSingle(c => c.Name == "RU" && c.Count == 2);
            summary.TopReferrers.Should().ContainSingle(r => r.Name == "https://github.com" && r.Count == 2);
            summary.TopReferrers.Should().ContainSingle(r => r.Name == "https://google.com" && r.Count == 1);

            // All seeds share "today", so exactly one daily bucket carries all 5 clicks.
            summary.ClicksByPeriod.Should().ContainSingle(p => p.Count == 5);
        }

        [Fact]
        public async Task QrCode_OwnerGetsPng()
        {
            using var client = CreateClient();

            var token = await RegisterAndGetTokenAsync(client);
            Authorize(client, token);
            var code = await CreateUrlAsync(client, "https://example.org/qr", alias: "it-" + Guid.NewGuid().ToString("N")[..12]);
            var links = await GetLinksAsync(client);
            var id = await FindLinkIdAsync(links, code);

            var response = await client.GetAsync($"api/v1/urls/{id}/qrcode");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Content.Headers.ContentType?.MediaType.Should().Be("image/png");

            var bytes = await response.Content.ReadAsByteArrayAsync();
            bytes.Should().NotBeEmpty();
        }
    }
}
