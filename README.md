# ShortenerUrlApp

A full-featured URL shortener built on ASP.NET Core (.NET 10) and Blazor WebAssembly:
click analytics, QR codes, JWT authentication, custom aliases, link expiration and click
limits, backed by PostgreSQL with Redis as the cache and write-behind buffer for the
redirect hot path.

## Features

- **Short link generation** — 7-character codes drawn from a Base57 alphabet using a
  cryptographically secure RNG, retried on collision and backed by a unique index on the
  short code.
- **HTTP 302 redirects** — the public `/{code}` route resolves from Redis first and falls
  back to PostgreSQL, so the redirect path does not hit the database on cache hits.
- **Custom aliases** — user-chosen codes of 3-20 characters (`[a-zA-Z0-9_-]`), validated for
  format and reserved route names (`health`, `api`, `openapi`, `swagger`, `scalar`);
  duplicates are rejected with `409 Conflict`.
- **Link expiration** — optional lifetime (`ExpiresInMinutes`, up to 1 year). Expired links
  stop redirecting with `410 Gone`, cached entries die no later than `ExpiresAt`, and a
  background worker sweeps the database and Redis every 5 minutes.
- **Click limits** — optional `MaxClicks` cap; once reached, the link returns `410 Gone`.
  Capped links are never cached, so the live Redis counter stays authoritative.
- **Click analytics** — per-link totals, clicks by day/week/month, top referrers and
  clicks-by-country breakdowns, with date-range filtering. Aggregation runs server-side in
  PostgreSQL via EF Core `GroupBy`, so large click volumes do not load the API process.
- **QR codes** — PNG codes (QRCoder, ECC level Q) for any owned link, cached in Redis for a
  day.
- **JWT authentication** — ASP.NET Core Identity accounts (email + password) issuing HS256
  bearer tokens with configurable issuer, audience and lifetime (60 minutes by default).
- **Ownership enforcement** — every protected operation is scoped to the current user;
  foreign or unknown links return `404`/empty results rather than leaking existence.
- **Write-behind counters** — click counters and click-event metadata
  (timestamp, IP, user agent, referer) are buffered in Redis on the redirect path and
  flushed to PostgreSQL in batches by background workers once a minute.
- **Background workers** — `ClickSyncWorker` (counters), `ClickEventSyncWorker` (analytics
  events, drained with a Redis `MULTI`/`EXEC` transaction), `ExpiredLinksCleanupWorker`.
- **API documentation** — OpenAPI document with a Scalar UI at `/scalar/v1`
  (Development environment).
- **Web dashboard** — Blazor WebAssembly client with MudBlazor: landing page, register,
  login, link table with CRUD, per-link analytics charts and QR download dialog.
- **Containers and CI** — multi-stage Dockerfiles with health checks, Docker Compose stack,
  and a GitHub Actions pipeline that builds the solution, runs 75 xUnit tests and verifies
  the API image build.
- **EF Core Code-First migrations** — applied automatically on API startup.

## Tech stack

| Component | Technology |
|---|---|
| Backend | ASP.NET Core Web API (.NET 10.0) |
| Frontend | Blazor WebAssembly + MudBlazor 7.x |
| API docs | OpenAPI + Scalar.AspNetCore 2.12.x |
| Database | PostgreSQL 16 (EF Core 10.0.11 + Npgsql 10.0.3) |
| Cache | Redis 7 (StackExchange.Redis 2.11.0) |
| Auth | ASP.NET Core Identity + JWT Bearer (HS256) |
| QR codes | QRCoder 1.6.0 |
| Tests | xUnit 2.9.3 + Moq 4.20.72 + FluentAssertions 8.8.0 (75 tests) |
| Container | Docker + Docker Compose (multi-stage, health checks) |
| CI/CD | GitHub Actions |

## Quick start

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) with Compose v2
- [Git](https://git-scm.com/)

### Run with Docker Compose

The Compose stack starts PostgreSQL, Redis, the API and the UI. The API applies
migrations on startup, so no manual database setup is required.

```bash
git clone https://github.com/ArchyAng83/ShortenerUrlApp.git
cd ShortenerUrlApp

# Create a local .env and set POSTGRES_PASSWORD / JWT_SECRET
cp .env.example .env          # PowerShell: Copy-Item .env.example .env

docker compose up -d --build
```

| Service | URL |
|---|---|
| API | http://localhost:5153 |
| Scalar API reference | http://localhost:5153/scalar/v1 |
| Health probe | http://localhost:5153/health |
| Web UI | http://localhost:5209 |
| PostgreSQL | localhost:5432 |
| Redis | localhost:6379 |

Useful Compose commands:

```bash
docker compose up -d postgres redis   # infrastructure only (for a local dotnet run)
docker compose logs -f api            # API logs
docker compose down                   # stop (data persists in named volumes)
docker compose down -v                # stop and drop the database/Redis volumes
```

### Run locally without containers

Set the required variables, then start the API and the UI in two terminals. The API fails
fast if a connection string or the JWT secret is missing.

```powershell
docker compose up -d postgres redis

$env:ConnectionStrings__DefaultConnection = "Host=localhost;Port=5432;Database=UrlShortenerDb;Username=postgres;Password=your_secure_password"
$env:ConnectionStrings__Redis = "localhost:6379"
$env:JwtSettings__Secret = "your-super-secret-key-minimum-32-characters-long"

dotnet run --project ShortenerUrl.WebApi --launch-profile https
```

```powershell
# second terminal
dotnet run --project ShortenerUrlApp.WebUI --launch-profile https
```

Notes:

- The UI must run on `https://localhost:7159` (the `https` profile): that is the only
  CORS origin allowed by the API. Adjust `AddCors` in
  `ShortenerUrl.WebApi/DependencyInjection.cs` if you use a different port.
- The API base URL for the UI comes from `ShortenerUrlApp.WebUI/wwwroot/appsettings.json`
  (`ApiBaseUrl`, defaulting to `http://localhost:5153`).
- `app.UseHttpsRedirection()` is currently disabled in `Program.cs`; the API serves plain
  HTTP on port 5153 and HTTPS on port 7019.

### Run tests

```bash
dotnet test ShortenerUrlApp.slnx
```

### Migrations

```bash
dotnet ef migrations add Description --project ShortenerUrl.WebApi --startup-project ShortenerUrl.WebApi
dotnet ef database update --project ShortenerUrl.WebApi --startup-project ShortenerUrl.WebApi
```

## API endpoints

Protected endpoints require an `Authorization: Bearer <token>` header.

### Public

| Method | Route | Description |
|---|---|---|
| `GET` | `/{code}` | Redirect to the long URL: `302` on success, `404` for unknown codes, `410 Gone` for expired links or links that hit their click cap. Each successful redirect records a click. |
| `GET` | `/health` | Liveness probe (`200 "Healthy"`), used by the Docker health check |
| `POST` | `/api/v1/auth/register` | Create an account and return a JWT (`400` with Identity errors otherwise) |
| `POST` | `/api/v1/auth/login` | Exchange credentials for a JWT (`401` for unknown email or wrong password) |

### Shortened URLs (authorized)

| Method | Route | Description |
|---|---|---|
| `GET` | `/api/ShortenerUrl` | All links of the current user, newest first |
| `POST` | `/api/ShortenerUrl` | Create a link; returns the short code (`400` invalid URL/alias, `409` alias taken) |
| `PUT` | `/api/ShortenerUrl` | Re-point an existing link to a new target URL, by `id` in the body (`404` if not owned) |
| `DELETE` | `/api/ShortenerUrl/{id}` | Delete a link and its Redis keys (`204` on success, `404` if not owned) |

### Analytics and QR codes (authorized)

| Method | Route | Description |
|---|---|---|
| `GET` | `/api/v1/urls/{id}/analytics` | Summary: `totalClicks`, `clicksByPeriod`, `topReferrers`, `clicksByCountry`. Query: `dateFrom`, `dateTo`, `groupBy=day\|week\|month` |
| `GET` | `/api/v1/urls/{id}/analytics/by-country` | Click counts grouped by country |
| `GET` | `/api/v1/urls/{id}/analytics/by-referrers` | Top referrers. Query: `top` (1-100, default 10) |
| `GET` | `/api/v1/urls/{id}/qrcode` | PNG QR code of the short URL (`image/png`) |

Analytics and QR endpoints return `404` (or empty aggregates) for links the caller does not
own. Unauthenticated calls return `401`.

### Route versioning note

Link CRUD is routed by controller name at `/api/ShortenerUrl`, while auth, analytics and QR
codes live under the versioned `/api/v1/...` prefix.

### Examples

Register and log in:

```bash
curl -X POST http://localhost:5153/api/v1/auth/register \
  -H "Content-Type: application/json" \
  -d '{"userName":"demo","email":"demo@example.com","password":"Passw0rd"}'

curl -X POST http://localhost:5153/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"demo@example.com","password":"Passw0rd"}'
```

Create a custom alias that expires in 24 hours, cap it at 100 clicks, then read analytics:

```bash
curl -X POST http://localhost:5153/api/ShortenerUrl \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"longUrl":"https://example.com/very/long/path","customAlias":"demo-link","expiresInMinutes":1440,"maxClicks":100}'

curl "http://localhost:5153/api/v1/urls/$ID/analytics?groupBy=week" \
  -H "Authorization: Bearer $TOKEN"
```

## Project structure

```
ShortenerUrlApp/
├── ShortenerUrl.WebApi/              # REST API + Identity/JWT + background workers
│   ├── Constants/                    # Code alphabet, alias and TTL limits
│   ├── Controllers/                  # ShortenerUrlController, AuthController,
│   │                                 # AnalyticsController, QRCodeController
│   ├── Data/                         # ShortenerUrlDbContext + EF Core migrations
│   ├── Entities/                     # ShortenerUrl, ClickEvent, ApplicationUser
│   ├── Services/                     # Shortener, auth, analytics, QR services + workers
│   ├── DependencyInjection.cs        # DbContext, Redis, CORS, Identity, JWT, services
│   ├── Dockerfile                    # Multi-stage build (restore, build, test, publish)
│   ├── Program.cs                    # Pipeline, /health and /{code} redirect endpoints
│   └── appsettings.json              # Empty connection strings, JWT defaults
│
├── ShortenerUrlApp.Shared/           # Contracts shared by API and client
│   ├── DTOs/                         # Request/response records (URLs, auth, analytics)
│   └── Validators/                   # HttpUrlAttribute
│
├── ShortenerUrlApp.WebUI/            # Blazor WebAssembly client (MudBlazor)
│   ├── Pages/                        # Home, Login, Register, Dashboard, UrlDetails
│   ├── Components/                   # CreateUrlDialog, QRCodeDialog, ConfirmDialog
│   ├── Layout/                       # MainLayout
│   ├── Services/                     # AuthService, JwtAuthStateProvider,
│   │                                 # AuthorizationMessageHandler, helpers
│   ├── wwwroot/                      # index.html, appsettings.json (ApiBaseUrl), assets
│   ├── Dockerfile                    # Build + nginx static hosting
│   └── nginx.conf                    # SPA fallback, asset caching, security headers
│
├── ShortenerUrlApp.Tests/            # xUnit tests for services and controllers
├── .github/workflows/ci.yml          # Build, test and Docker image verification
├── docker-compose.yml                # postgres, redis, api, ui
├── .env.example                      # Compose environment template
├── .editorconfig                     # C# formatting and analyzer conventions
└── ShortenerUrlApp.slnx              # Solution
```

## Configuration

Copy `.env.example` to `.env` and adjust the values; Compose reads it automatically and
maps the variables onto the API configuration (`ConnectionStrings__*`, `JwtSettings__*`).

| Variable | Default | Purpose |
|---|---|---|
| `POSTGRES_USER` | `postgres` | PostgreSQL role |
| `POSTGRES_PASSWORD` | `your_secure_password` | PostgreSQL password — change it |
| `POSTGRES_DB` | `UrlShortenerDb` | Database name |
| `REDIS_URL` | `redis:6379` | Redis endpoint (reference value; Compose wires `ConnectionStrings__Redis`) |
| `JWT_SECRET` | `your-super-secret-key-minimum-32-characters-long` | HS256 signing key, minimum 32 characters — change it |
| `JWT_ISSUER` | `ShortenerUrlApp` | Expected token issuer |
| `JWT_AUDIENCE` | `ShortenerUrlApp` | Expected token audience |
| `JWT_EXPIRY_MINUTES` | `60` | Token lifetime |
| `ASPNETCORE_ENVIRONMENT` | `Development` | Enables the OpenAPI document and Scalar UI |
| `API_PORT` | `5153` | Host port mapped to the API container |
| `UI_PORT` | `5209` | Host port mapped to the UI container |

Required API settings (fail fast when missing or too short): `ConnectionStrings:DefaultConnection`,
`ConnectionStrings:Redis`, `JwtSettings:Secret`. No credentials are committed; the defaults
in `appsettings.json` are placeholders.

## CI

`.github/workflows/ci.yml` runs on pushes and pull requests to `main`:

1. `build` — restore, Release build of the whole solution, `dotnet test` with TRX results
   uploaded as an artifact.
2. `docker` — after a green build, verify the API image builds (Buildx, GHA layer cache,
   no push).

## License

MIT
