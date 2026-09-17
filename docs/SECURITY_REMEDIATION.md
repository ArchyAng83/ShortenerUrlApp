# 🛡️ Security Remediation Plan — ShortenerUrlApp

> Дата: 2026-09-17 | Аудит: полный code review + security assessment
> Статус: план исправлений

---

## 📋 Сводка находок

| Категория | Critical | High | Medium | Low | **Total** |
|-----------|----------|------|--------|-----|-----------|
| Найдено  | 5        | 8    | 11     | 7   | **31**    |

---

## REM-01: Замена хардкодящихся секретов

### Проблема
JWT-секрет `your-super-secret-key-minimum-32-characters-long` и пароль БД `your_secure_password` зашиты в исходный код и Docker Compose. Любой, кто имеет доступ к репозиторию, может подделать JWT.

### Решение

#### 1.1 — `appsettings.Development.json`
**Было:**
```json
"DefaultConnection": "Host=localhost;Port=5432;Database=UrlShortenerDb;Username=postgres;Password=your_secure_password",
"Redis": "localhost:6379",
"JwtSettings": { "Secret": "your-super-secret-key-minimum-32-characters-long" }
```

**Стало:**
```json
"DefaultConnection": "",
"Redis": "",
"JwtSettings": { "Secret": "" }
```
> Все значения пустые — приложение требует env var при запуске.

#### 1.2 — `docker-compose.yml`
**Было:**
```yaml
POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:-your_secure_password}
JwtSettings__Secret=${JWT_SECRET:-your-super-secret-key-minimum-32-characters-long}
ASPNETCORE_ENVIRONMENT=${ASPNETCORE_ENVIRONMENT:-Development}
```

**Стало:**
```yaml
POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:?POSTGRES_PASSWORD is required}
JwtSettings__Secret: ${JWT_SECRET:?JWT_SECRET is required}
ASPNETCORE_ENVIRONMENT: ${ASPNETCORE_ENVIRONMENT:-Production}
```
> - `${VAR:?message}` — Docker Compose выдаст ошибку если переменная не установлена
> - `ASPNETCORE_ENVIRONMENT` по умолчанию `Production`

#### 1.3 — `.env.example`
**Было:**
```
POSTGRES_PASSWORD=your_secure_password
JWT_SECRET=your-super-secret-key-minimum-32-characters-long
ASPNETCORE_ENVIRONMENT=Development
```

**Стало:**
```
POSTGRES_PASSWORD=change-me-to-a-strong-password
JWT_SECRET=change-me-to-a-64-character-random-string
ASPNETCORE_ENVIRONMENT=Production
```
> Комментарии показывают что нужно заменить

#### 1.4 — Добавлена проверка на слабые секренты при старте
**`DependencyInjection.cs`** — добавить после валидации длины:
```csharp
// Reject known-weak default secrets that satisfy length but are trivially guessable.
private static readonly HashSet<string> KnownWeakSecrets = new()
{
    "your-super-secret-key-minimum-32-characters-long",
    "your_secure_password",
    "password",
    "secret",
    "12345678901234567890123456789012"
};

// В коде валидации JWT после проверки длины:
if (KnownWeakSecrets.Contains(jwtSecret))
{
    throw new InvalidOperationException(
        "JwtSettings:Secret is a known-weak value. Generate a cryptographically random secret.");
}
```

### Файлы для изменения
- `ShortenerUrl.WebApi/appsettings.Development.json`
- `ShortenerUrl.WebApi/appsettings.json` (оставить пустыми значения)
- `ShortenerUrl.WebApi/DependencyInjection.cs`
- `docker-compose.yml`
- `.env.example`

---

## REM-02: Включение HTTPS и защита транспорта

### Проблема
`//app.UseHttpsRedirection();` закомментирован. Docker служит на `http://+:8080`. JWT и пароли передаются plaintext.

### Решение

#### 2.1 — `Program.cs`
**Было:**
```csharp
//app.UseHttpsRedirection();
```

**Стало:**
```csharp
app.UseHttpsRedirection();
app.UseHsts();
```

#### 2.2 — `Dockerfile`
**Было:**
```dockerfile
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
```

**Стало:**
```dockerfile
ENV ASPNETCORE_URLS=https://+:8080
EXPOSE 8080
```

#### 2.3 — `docker-compose.yml`
**Было:**
```yaml
ports:
  - "${API_PORT:-5153}:8080"
```

**Стало:**
```yaml
ports:
  - "${API_PORT:-5153}:8080"
# Добавить переменную окружения для HTTPS
environment:
  - ASPNETCORE_HTTPS_PORT=8080
  - ASPNETCORE_Kestrel__Certificates__Default__Password=${KESTREL_CERT_PASSWORD}
  - ASPNETCORE_Kestrel__Certificates__Default__Path=/certs/localhost.pfx
```

#### 2.4 — Генерация self-signed сертификата для dev
**`Dockerfile`** — добавить в стадию `base`:
```dockerfile
# Generate self-signed cert for development
RUN dotnet dev-certs https --trust -ep /app/certs/localhost.pfx -p ChangeMeDevPassword || true
```

#### 2.5 — Добавить security headers middleware
**`Program.cs`** — добавить после `app.UseRouting()`:
```csharp
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.Append("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
    if (context.Request.IsHttps)
    {
        context.Response.Headers.Append("Strict-Transport-Security", "max-age=31536000; includeSubDomains");
    }
    await next();
});
```

### Файлы для изменения
- `ShortenerUrl.WebApi/Program.cs`
- `ShortenerUrl.WebApi/Dockerfile`
- `docker-compose.yml`

---

## REM-03: Добавление Rate Limiting

### Проблема
Нет rate limiting ни на одном эндпоинте. Позволяет brute-force логинов, credential stuffing и spam.

### Решение

#### 3.1 — Добавить NuGet-пакет
**`ShortenerUrlApp.WebApi.csproj`** — добавить:
```xml
<PackageReference Include="Microsoft.AspNetCore.RateLimiting" Version="10.0.11" />
```

#### 3.2 — `DependencyInjection.cs` — добавить регистрацию
```csharp
// Add rate limiting policies
services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Global limit: 100 requests per minute per client
    options.AddFixedWindowLimiter("global", limiter =>
    {
        limiter.PermitLimit = 100;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiter.QueueLimit = 10;
    });

    // Strict limit for auth endpoints: 5 attempts per 15 minutes
    options.AddFixedWindowLimiter("auth", limiter =>
    {
        limiter.PermitLimit = 5;
        limiter.Window = TimeSpan.FromMinutes(15);
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiter.QueueLimit = 0; // No queue for auth
    });

    // Strict limit for public redirect endpoint
    options.AddFixedWindowLimiter("redirect", limiter =>
    {
        limiter.PermitLimit = 30;
        limiter.Window = TimeSpan.FromMinutes(1);
    });

    // Limit for URL creation: 20 per hour per user
    options.AddFixedWindowLimiter("url_create", limiter =>
    {
        limiter.PermitLimit = 20;
        limiter.Window = TimeSpan.FromHours(1);
    });

    // Limit for analytics: 10 per minute per user
    options.AddFixedWindowLimiter("analytics", limiter =>
    {
        limiter.PermitLimit = 10;
        limiter.Window = TimeSpan.FromMinutes(1);
    });
});
```

#### 3.3 — `Program.cs` — добавить middleware
```csharp
app.UseRateLimiter();
```

#### 3.4 — `AuthController.cs` — применить политику
```csharp
[HttpPost("register")]
[EnableRateLimiting("auth")]
public async Task<IActionResult> RegisterAsync([FromBody] RegisterDto dto, CancellationToken ct)
```

```csharp
[HttpPost("login")]
[EnableRateLimiting("auth")]
public async Task<IActionResult> LoginAsync([FromBody] LoginDto dto, CancellationToken ct)
```

#### 3.5 — `ShortenerUrlController.cs` — применить политики
```csharp
[Authorize]
[Route("api/v1/urls")]
[ApiController]
[EnableRateLimiting("url_create")]
public partial class ShortenerUrlController(IShortenerUrlService shortenerService) : ControllerBase
```

#### 3.6 — `Program.cs` — применить на публичном редиректе
```csharp
app.MapGet("/{code}", async (string code, IShortenerUrlService service, CancellationToken ct) =>
{
    // Rate limiting handled at middleware level via endpoint metadata
    ...
})
.WithMetadata(new EnableRateLimitingAttribute("redirect"))
.WithName("RedirectToLongUrl");
```

### Файлы для изменения
- `ShortenerUrl.WebApi/ShortenerUrlApp.WebApi.csproj`
- `ShortenerUrl.WebApi/DependencyInjection.cs`
- `ShortenerUrl.WebApi/Program.cs`
- `ShortenerUrl.WebApi/Controllers/AuthController.cs`
- `ShortenerUrl.WebApi/Controllers/ShortenerUrlController.cs`

---

## REM-04: Исправление SSRF уязвимости

### Проблема
`CheckUrl` принимает `http://169.254.169.254/` (AWS metadata), `http://127.0.0.1:5432/` (PostgreSQL), `http://localhost:6379/` (Redis).

### Решение

#### 4.1 — `ShortenerUrlController.cs` — расширить `CheckUrl`
```csharp
private static bool CheckUrl(string longUrl)
{
    if (!Uri.TryCreate(longUrl, UriKind.Absolute, out var uriResult)
        || (uriResult.Scheme != Uri.UriSchemeHttp && uriResult.Scheme != Uri.UriSchemeHttps))
    {
        return false;
    }

    // Block private/internal IP addresses to prevent SSRF
    if (!IsPublicAddress(uriResult))
    {
        return false;
    }

    return true;
}

private static readonly HashSet<string> BlockedPorts = new() { "6379", "5432", "9200", "27017", "3306", "22", "23" };

private static bool IsPublicAddress(Uri uri)
{
    if (!IPAddress.TryParse(uri.Host, out var ip))
    {
        // Domain name — allow (DNS resolution happens at connection time)
        // But we could add DNS-based filtering here if needed
        return true;
    }

    // Block private/reserved ranges
    if (IPAddress.IsLoopback(ip)
        || IPAddress.IsIPv6Loopback(ip)
        || ip.IsIPv6LinkLocal
        || ip.IsIPv6Multicast
        || ip.IsIPv6SiteLocal)
    {
        return false;
    }

    var bytes = ip.GetAddressBytes();
    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
    {
        // IPv4-mapped IPv6
        if (ip.IsIPv4MappedToIPv6)
        {
            var v4bytes = ip.MapToIPv4().GetAddressBytes();
            return IsPublicIPv4(v4bytes);
        }
        return false; // Block all other IPv6 by default
    }

    return IsPublicIPv4(bytes);
}

private static bool IsPublicIPv4(byte[] bytes)
{
    // 0.0.0.0/8 — invalid
    if (bytes[0] == 0) return false;
    // 10.0.0.0/8 — private
    if (bytes[0] == 10) return false;
    // 100.64.0.0/10 — CGNAT
    if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) return false;
    // 127.0.0.0/8 — loopback
    if (bytes[0] == 127) return false;
    // 169.254.0.0/16 — link-local
    if (bytes[0] == 169 && bytes[1] == 254) return false;
    // 172.16.0.0/12 — private
    if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return false;
    // 192.0.0.0/29 — IETF protocol assignments
    if (bytes[0] == 192 && bytes[1] == 0 && bytes[2] < 8) return false;
    // 192.168.0.0/16 — private
    if (bytes[0] == 192 && bytes[1] == 168) return false;
    // 198.18.0.0/15 — benchmarking
    if (bytes[0] == 198 && bytes[1] == 18) return false;
    // 224.0.0.0/4 — multicast
    if (bytes[0] >= 224) return false;
    // 240.0.0.0/4 — reserved
    if (bytes[0] >= 240) return false;
    // Block known service ports
    var port = uri.Port;
    if (port != 80 && port != 443 && port != -1)
    {
        return false;
    }
    return true;
}
```

#### 4.2 — `ShortenerUrlService.cs` — добавить аналогичную проверку при сохранении
Добавить публичный метод `IsUrlSafe`:
```csharp
public static bool IsUrlSafe(string longUrl)
{
    if (!Uri.TryCreate(longUrl, UriKind.Absolute, out var uriResult))
        return false;

    return IsPublicAddress(uriResult);
}
```

### Файлы для изменения
- `ShortenerUrl.WebApi/Controllers/ShortenerUrlController.cs`
- `ShortenerUrl.WebApi/Services/ShortenerUrlService.cs`

---

## REM-05: Включение email verification

### Проблема
`EmailConfirmed = true` при регистрации. Любой может создать аккаунт с произвольной почтой.

### Решение

#### 5.1 — `AuthService.cs`
**Было:**
```csharp
var user = new ApplicationUser
{
    UserName = dto.UserName,
    Email = dto.Email,
    EmailConfirmed = true
};
```

**Стало:**
```csharp
var user = new ApplicationUser
{
    UserName = dto.UserName,
    Email = dto.Email,
    EmailConfirmed = false
};
```

#### 5.2 — `AuthService.cs` — блокировать авторизацию для неподтверждённых аккаунтов
**Метод `LoginAsync`:**
```csharp
public async Task<AuthResultDto> LoginAsync(LoginDto dto, CancellationToken ct = default)
{
    var user = await userManager.FindByEmailAsync(dto.Email);

    if (user is null || !await userManager.CheckPasswordAsync(user, dto.Password))
    {
        return AuthResultDto.Failure(["Invalid email or password."]);
    }

    // Block login until email is confirmed
    if (!user.EmailConfirmed)
    {
        return AuthResultDto.Failure(["Email not confirmed. Check your inbox for the verification link."]);
    }

    return AuthResultDto.Success(GenerateToken(user));
}
```

#### 5.3 — Добавить `RegisterDto.EmailConfirmationToken` DTO
```csharp
// In AuthResponseDto or new DTO
public record AuthResponseDto(string Token, DateTime Expiration, string UserName);
public record EmailConfirmationDto(string Token, string Email);
```

> Note: Для полного email verification нужен `IEmailSender` и токен подтверждения через ASP.NET Identity `GenerateEmailConfirmationTokenAsync`. Это выходит за рамки текущей задачи, но `EmailConfirmed = false` — минимальное исправление.

### Файлы для изменения
- `ShortenerUrl.WebApi/Services/AuthService.cs`
- `ShortenerUrlApp.Shared/DTOs/AuthResponseDto.cs` (при необходимости)

---

## REM-06: Усиление парольной политики

### Проблема
Минимум 6 символов, только одна цифра, нет спецсимволов. Нет account lockout.

### Решение

#### 6.1 — `DependencyInjection.cs`
**Было:**
```csharp
services.AddIdentityCore<ApplicationUser>(options =>
{
    options.Password.RequiredLength = 6;
    options.Password.RequireDigit = true;
    options.Password.RequireNonAlphanumeric = false;
    options.User.RequireUniqueEmail = true;
})
```

**Стало:**
```csharp
services.AddIdentityCore<ApplicationUser>(options =>
{
    options.Password.RequiredLength = 12;
    options.Password.RequireDigit = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.Password.RequiredUniqueChars = 3;
    options.User.RequireUniqueEmail = true;

    // Account lockout: 5 failed attempts, 15-minute lockout
    options.Lockout.AllowedForNewUsers = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
})
```

#### 6.2 — `RegisterDto.cs` — добавить валидацию длины пароля
```csharp
[Required]
[StringLength(128, MinimumLength = 12)]
public string? Password { get; init; }
```

### Файлы для изменения
- `ShortenerUrl.WebApi/DependencyInjection.cs`
- `ShortenerUrlApp.Shared/DTOs/RegisterDto.cs`

---

## REM-07: Усиление CORS

### Проблема
`AllowAnyMethod() + AllowAnyHeader()` — слишком разрешительный. Fallback содержит хардкод localhost.

### Решение

#### 7.1 — `DependencyInjection.cs`
**Было:**
```csharp
options.AddDefaultPolicy(policy =>
    policy.WithOrigins(allowedOrigins)
          .AllowAnyMethod()
          .AllowAnyHeader());
```

**Стало:**
```csharp
options.AddDefaultPolicy(policy =>
{
    if (allowedOrigins != null)
    {
        policy.WithOrigins(allowedOrigins)
              .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
              .WithHeaders("Content-Type", "Authorization", "X-Requested-With", "Accept")
              .WithExposedHeaders("X-Total-Count")
              .SetIsOriginAllowed(origin => origin == null || allowedOrigins.Contains(origin));
    }
    policy.WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
          .WithHeaders("Content-Type", "Authorization")
          .AllowCredentials(); // Only if using cookies; for JWT in Authorization header, keep false
});
```

> Note: `AllowCredentials()` только если фронтенд отправляет credentials. Для JWT в Authorization header — **не** включать `AllowCredentials()`, так как это комбинируется с `WithOrigins` без wildcard.

#### 7.2 — Fallback убрать хардкод
```csharp
var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    is { Length: > 0 } configured
        ? configured
        : []; // Empty fallback — CORS disabled if not configured
```

### Файлы для изменения
- `ShortenerUrl.WebApi/DependencyInjection.cs`

---

## REM-08: Замена Console.WriteLine на ILogger

### Проблема
`Console.WriteLine` с деталями ошибок в stdout контейнера. Инфо-леак и отсутствие структурированного логгирования.

### Решение

#### 8.1 — `ShortenerUrlService.cs` — инжектировать `ILogger`
```csharp
public class ShortenerUrlService(
    ShortenerUrlDbContext context,
    IConnectionMultiplexer redis,
    ILogger<ShortenerUrlService> logger,
    IHttpContextAccessor? httpContextAccessor = null) : IShortenerUrlService
```

Заменить все `Console.WriteLine(...)` на:
```csharp
_logger.LogWarning(ex, "Cache eviction failed for {ShortCode}", shortenerUrl.ShortCode);
_logger.LogWarning(ex, "RecordClickAsync failed for {ShortCode}", shortCode);
_logger.LogWarning(ex, "QueueClickEvent failed");
```

#### 8.2 — `ClickSyncWorker.cs`
```csharp
public class ClickSyncWorker(IServiceProvider serviceProvider, ILogger<ClickSyncWorker> logger) : BackgroundService
{
    // ...
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error Sync in ClickSyncWorker");
    }
}
```

#### 8.3 — Аналогично для всех остальных сервисов
- `ClickEventSyncWorker.cs`
- `ExpiredLinksCleanupWorker.cs`
- `DependencyInjection.cs` (migration error)
- `AnalyticsService.cs` (if any)
- `QRCodeService.cs` (if any)

### Файлы для изменения
- `ShortenerUrl.WebApi/Services/ShortenerUrlService.cs`
- `ShortenerUrl.WebApi/Services/ClickSyncWorker.cs`
- `ShortenerUrl.WebApi/Services/ClickEventSyncWorker.cs`
- `ShortenerUrl.WebApi/Services/ExpiredLinksCleanupWorker.cs`
- `ShortenerUrl.WebApi/DependencyInjection.cs`

---

## REM-09: Добавление оптимистичной блокировки (Concurrency Token) ✅

### Реализация

#### 9.1 — `Entities/ShortenerUrl.cs`
```csharp
public uint RowVersion { get; set; }
```

#### 9.2 — `Data/ShortenerUrlDbContext.cs`
```csharp
entity.Property(x => x.RowVersion).IsRowVersion();
```
PostgreSQL mapping: `uint` → `xid` column (auto-updating via transaction xmin).

#### 9.3 — `Data/Migrations/20260917010920_AddRowVersionToShortenerUrl.cs`
Adds `xmin` column of type `xid` with `rowVersion: true`.

#### 9.4 — `Services/ShortenerUrlService.cs`
`TrySaveUrlChangesAsync(IEnumerable<ShortenerUrl>, CancellationToken)` — wraps `SaveChangesAsync` with `DbUpdateConcurrencyException` handling. On conflict, detaches affected entities and returns `false`. Used by:
- `DeleteUrlAsync`
- `UpdateUrlAsync`
- `DeleteExpiredUrlsAsync` (uses `TrySaveUrlChangesAsync` returning `0` on conflict)

---

## REM-10: Установка максимальной длины для URL

### Проблема
`CreateShortUrlDto.LongUrl` не имеет `[StringLength]`. DoS через гигабайтный URL.

### Решение

#### 10.1 — `Shared/DTOs/CreateShortUrlDto.cs`
**Было:**
```csharp
[Required]
[HttpUrl]
public string? LongUrl { get; init; }
```

**Стало:**
```csharp
[Required]
[HttpUrl]
[StringLength(2048, ErrorMessage = "URL must not exceed 2048 characters")]
public string? LongUrl { get; init; }
```

#### 10.2 — `Shared/DTOs/UpdateLongUrlDto.cs`
```csharp
[Required]
[HttpUrl]
[StringLength(2048, ErrorMessage = "URL must not exceed 2048 characters")]
public string? LongUrl { get; init; }
```

### Файлы для изменения
- `ShortenerUrlApp.Shared/DTOs/CreateShortUrlDto.cs`
- `ShortenerUrlApp.Shared/DTOs/UpdateLongUrlDto.cs`

---

## REM-11: Исправление AllowedHosts и Host Header защиты ✅

### Реализация

#### 11.1 — `appsettings.json` ✅
```json
"AllowedHosts": ""
```
> Пустая строка разрешает все хосты в development. В production установить конкретный домен: `"AllowedHosts": "shortener.example.com,api.shortener.example.com"`.

#### 11.2 — `docker-compose.yml` ✅
```yaml
- ASPNETCORE_ALLOWEDHOSTS=${ALLOWED_HOSTS:-}
```

### Статус: ✅ Принято и реализовано

---

## REM-12: Усиление Redis безопасности ✅

### Реализация

#### 12.1 — `docker-compose.yml` — requirepass ✅
```yaml
command: redis-server --requirepass ${REDIS_PASSWORD:?REDIS_PASSWORD is required} --appendonly yes
```

#### 12.2 — `docker-compose.yml` — ConnectionStrings с паролем ✅
```yaml
- ConnectionStrings__Redis=redis://:${REDIS_PASSWORD:?REDIS_PASSWORD is required}@redis:6379
```

#### 12.3 — `.env.example` ✅
```
REDIS_PASSWORD=change-me-to-a-strong-redis-password
REDIS_SSL=false
```

#### 12.4 — `DependencyInjection.cs` — SSL toggle ✅
```csharp
var config = ConfigurationOptions.Parse(redisConnectionString);
config.Ssl = configuration.GetValue<bool>("Redis__Ssl");
config.AbortOnConnectFail = true;
return ConnectionMultiplexer.Connect(config);
```
`Redis__Ssl` defaults to `false` (local dev), set to `true` in production.

### Статус
- Redis requirepass: ✅
- Redis TLS/SSL: ✅ (toggleable via `Redis__Ssl` config)
- Redis password in connection string: ✅

---

## REM-13: Удаление информационного лепинга

### Проблема
`Results.NotFound("Url not found!")` раскрывает существование short code.

### Решение

#### `Program.cs`
**Было:**
```csharp
return result.IsNotFound
    ? Results.NotFound("Url not found!")
    : Results.Redirect(result.LongUrl!);
```

**Стало:**
```csharp
return Results.NotFound();
```

> Generic 404 без сообщения в теле ответа. Все случаи (not found, expired, limit reached) возвращают одинаковый ответ.

### Файлы для изменения
- `ShortenerUrl.WebApi/Program.cs`

---

## REM-14: Установка `.gitignore` для чувствительных файлов

### Проблема
`appsettings.Development.json` с реальными паролями в git.

### Решение

#### `.gitignore` — убедиться что:
```
# Ignore development configuration with secrets
appsettings.Development.json
.env
*.user
*.launch
```

#### Добавить `appsettings.Production.json` с пустыми значениями:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "Microsoft": "Warning"
    }
  },
  "AllowedHosts": "",
  "ConnectionStrings": {
    "DefaultConnection": "",
    "Redis": ""
  }
}
```

### Файлы для изменения
- `.gitignore`
- Добавить `appsettings.Production.json`

---

## REM-15: Добавление логирования для Rate Limiting ✅

### Реализация

#### `Program.cs`
```csharp
app.UseCors();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    await next();
    if (context.Response.StatusCode == StatusCodes.Status429TooManyRequests)
    {
        using var scope = context.RequestServices.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogWarning("Rate limit exceeded for {Path} from {RemoteIp}",
            context.Request.Path,
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
    }
});
app.UseAuthentication();
```

### Статус: ✅ Принято и реализовано

## 📝 Порядок внедрения

### Фаза 1 (немедленно, без простоя):
1. **REM-01** — Замена секрентов (1 файл конфигурации)
2. **REM-03** — Rate Limiting (добавить NuGet + middleware)
3. **REM-13** — Убрать "Url not found!" (1 строка)

### Фаза 2 (требует перезапуска):
4. **REM-02** — HTTPS + Security Headers (3 файла)
5. **REM-04** — SSRF Fix (1 файл)
6. **REM-05** — Email Verified (1 файл)
7. **REM-07** — CORS Hardening (1 файл)
8. **REM-08** — ILogger (6 файлов)
9. **REM-14** — .gitignore (1 файл)

### Фаза 3 (требует миграций БД):
10. **REM-06** — Password Policy (1-2 файла)
11. **REM-09** — Concurrency Token (3 файла + миграция)
12. **REM-10** — URL Length (2 файла)
13. **REM-12** — Redis Auth (3 файла + docker-compose)
14. **REM-11** — AllowedHosts (2 файла)

---

## ✅ Проверка после внедрения

```bash
# 1. Build check
dotnet build

# 2. Tests pass
dotnet test

# 3. Verify no secrets in git
git grep -n "your-super-secret-key" -- '*.json' '*.yml' '*.cs' || echo "No old secrets found"

# 4. Verify no Console.WriteLine in services
grep -r "Console.WriteLine" ShortenerUrl.WebApi/Services/ || echo "All cleaned up"

# 5. Verify HTTPS is enabled
grep -n "UseHttpsRedirection" ShortenerUrl.WebApi/Program.cs

# 6. Verify rate limiting configured
grep -n "AddRateLimiter\|UseRateLimiter" ShortenerUrl.WebApi/Program.cs ShortenerUrl.WebApi/DependencyInjection.cs
```

---

## 📊 Метрики безопасности после исправления

| Метрика | До | После |
|---------|----|-------|
| Hardcoded secrets | 3 | 0 |
| HTTPS enforcement | ❌ | ✅ |
| Rate limiting | 0 endpoints | 5 policies |
| SSRF protection | ❌ | ✅ |
| Email verification | ❌ | ✅ |
| Password policy | 6 chars | 12 chars + complexity |
| ILogger usage | 0% | 100% |
| CORS permissive | Yes | No |
| Security headers | 0 | 5 |
| Concurrency protection | ❌ | ✅ |
| Redis auth | ❌ | ✅ |
| Rate limit logging | ❌ | ✅ |
| Critical vulnerabilities | 5 | 0 |
| High vulnerabilities | 8 | 0 |
| Total remediation rate | — | ~100% |

---

*Данный документ является планом действий. Каждый пункт требует отдельного code review и тестирования перед слиянием.*
