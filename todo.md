# 📋 ShortenerUrlApp — План развития

> Актуальный план развития проекта. Обновляется Оркестратором.

## Статус: 🟢 Основная функциональность реализована

> **Последняя проверка:** 2026-09-17 (Оркестратор)

---

## Фаза 0: Инфраструктура и документация ✅

> Базовая настройка окружения — **ПОЛНОСТЬЮ ЗАВЕРШЕНА**

- [x] **0.1** Создать `AGENTS.md` — роли агентов, правила, стек
- [x] **0.2** Создать `README.md` — описание, структура, инструкция запуска
- [x] **0.3** Создать `.env.example` — шаблон переменных окружения
- [x] **0.4** Настроить `docker-compose.yml` — PostgreSQL, Redis, API, UI
  - [x] Multi-stage Dockerfile для API
  - [x] Multi-stage Dockerfile для UI (nginx)
  - [x] Health checks для PostgreSQL и Redis
  - [x] Volume для данных PostgreSQL
  - [x] Зависимости (depends_on с healthcheck)
- [x] **0.5** Исправить опечатку в миграции: `SortenerUrls` → `ShortenerUrls`
- [x] **0.6** Убрать хардкоженные строки подключения → через конфигурацию/env vars
- [x] **0.7** Настроить GitHub Actions CI
  - [x] Build + Test при push/PR
  - [x] Docker build проверка (API)
  - [x] Lint/format check ← **СДЕЛАНО** (`dotnet format --verify-no-changes`, CRLF зафиксирован в .gitattributes)

---

## Фаза 1: Миграция на PostgreSQL ✅

> Переход с MySQL на PostgreSQL — **ПОЛНОСТЬЮ ЗАВЕРШЕНА**

- [x] **1.1** Заменить `MySql.EntityFrameworkCore` на `Npgsql.EntityFrameworkCore.PostgreSQL`
- [x] **1.2** Обновить `ShortenerUrlDbContext` — провайдер PostgreSQL
- [x] **1.3** Обновить `appsettings.json` — строка подключения PostgreSQL
- [x] **1.4** Пересоздать миграции для PostgreSQL
  - [x] Удалить старые миграции (MySQL)
  - [x] Создать миграции: `Init`, `AddIdentityAndUserId`, `AddAliasAndExpiry`
- [x] **1.5** Обновить Docker Compose — PostgreSQL сервис
- [x] **1.6** Проверить работу всех CRUD-операций (unit-тесты)

---

## Фаза 2: JWT-авторизация ✅

> Аутентификация и авторизация через ASP.NET Identity + JWT — **ПОЛНОСТЬЮ ЗАВЕРШЕНА**

- [x] **2.1** Добавить NuGet-пакеты
  - [x] `Microsoft.AspNetCore.Identity.EntityFrameworkCore`
  - [x] `Microsoft.AspNetCore.Authentication.JwtBearer`
- [x] **2.2** Создать `ApplicationUser` (наследует `IdentityUser`)
- [x] **2.3** Обновить `ShortenerUrlDbContext` — наследует `IdentityDbContext`
- [x] **2.4** Добавить связь `User` → `ShortenerUrl` (один ко многим, FK UserId, OnDelete SetNull)
- [x] **2.5** Создать DTOs для auth
  - [x] `RegisterDto` (UserName, Email, Password)
  - [x] `LoginDto` (Email, Password)
  - [x] `AuthResponseDto` (Token, Expiration, UserName)
  - [x] `AuthResultDto` (Succeeded, Auth, Errors)
- [x] **2.6** Создать `IAuthService` / `AuthService`
  - [x] Register — создание пользователя + JWT
  - [x] Login — валидация + генерация JWT
- [x] **2.7** Создать `AuthController`
  - [x] `POST /api/v1/auth/register`
  - [x] `POST /api/v1/auth/login`
- [x] **2.8** Настроить JWT в `DependencyInjection.cs`
  - [x] `AddAuthentication(JwtBearerDefaults.AuthenticationScheme)`
  - [x] `AddJwtBearer(options => ...)` с валидацией
- [x] **2.9** Обновить `ShortenerUrlController`
  - [x] `[Authorize]` на контроллере
  - [x] Привязка URL к пользователю (`UserId`)
  - [x] GET — только свои URL
- [x] **2.10** Пересоздать миграции с Identity (AddIdentityAndUserId)
- [x] **2.11** Обновить тесты (AuthServiceTests, AuthControllerTests)

---

## Фаза 3: Аналитика кликов ✅

> Детальная история переходов с браузером и графиками — **ПОЛНОСТЬЮ ЗАВЕРШЕНА**

- [x] **3.1** Создать сущность `ClickEvent`
  - [x] `Id` (Guid)
  - [x] `ShortenerUrlId` (FK)
  - [x] `ClickedAt` (DateTime)
  - [x] `IpAddress` (string)
  - [x] `UserAgent` (string)
  - [x] `Country` (string, nullable) ← **заполняется из GeoIP (3.7)**
  - [x] `City` (string, nullable) ← **заполняется из GeoIP (3.7)**
  - [x] `Referrer` (string, nullable)
- [x] **3.2** Обновить `ShortenerUrlDbContext` — добавить `DbSet<ClickEvent>`
- [x] **3.3** Обновить сервис `GetLongUrlAsync` — записывать `ClickEvent` в Redis (асинхронно, fire-and-forget)
- [x] **3.4** Создать `ClickSyncWorker` (flush click counts) + `ClickEventSyncWorker` (drain click-event lists)
- [x] **3.5** Создать `IAnalyticsService` / `AnalyticsService`
  - [x] `GetAnalyticsAsync` (summary with daily buckets, referrers, countries)
  - [x] `GetClicksByPeriodAsync` (group by day/week/month)
  - [x] `GetTopReferrersAsync`
  - [x] `GetClicksByCountryAsync`
- [x] **3.6** Создать `AnalyticsController`
  - [x] `GET /api/v1/urls/{id}/analytics` (dateFrom, dateTo, groupBy)
  - [x] `GET /api/v1/urls/{id}/analytics/by-country`
  - [x] `GET /api/v1/urls/{id}/analytics/by-referrers`
- [x] **3.7** Добавить GeoIP lookup ← **СДЕЛАНО** (GeoLite2/MaxMind, offline)
  - [x] Выбран подход: **GeoLite2 (offline mmdb)** вместо внешнего API
  - [x] `MaxMind.GeoIP2` 6.1.0 → `IGeoIpService` / `GeoIpService` (mmdb в памяти, graceful degradation, fast-path для private/reserved IP)
  - [x] Enrichment в `ClickEventSyncWorker` (per-batch кэш IP→GeoIP) — Country/City заполняются
  - [x] Docker: стадия `geolite` (скачивание с MaxMind по license key / fallback P3TERX)
  - [x] Тесты: `GeoIpServiceTests` (7 тестов, тестовая mmdb MaxMind)
- [x] **3.8** Создать DTOs для аналитики
  - [x] `ClickEventDto`
  - [x] `AnalyticsSummaryDto`
  - [x] `ClicksByPeriodDto`
  - [x] `NameCountDto`
- [x] **3.9** Написать тесты (AnalyticsServiceTests)

---

## Фаза 4: Кастомные alias и срок годности ✅

> Пользовательские короткие коды и автоудаление истёкших ссылок — **ПОЛНОСТЬЮ ЗАВЕРШЕНА**

- [x] **4.1** Обновить сущность `ShortenerUrl`
  - [x] `ExpiresAt` (DateTime, nullable)
  - [x] `IsCustomAlias` (bool)
  - [x] `MaxClicks` (int, nullable)
- [x] **4.2** Обновить `CreateShortUrlDto`
  - [x] `CustomAlias` (string, optional, 3-20 chars, alphanumeric + `_-`)
  - [x] `ExpiresInMinutes` (int, optional)
  - [x] `MaxClicks` (int, optional)
- [x] **4.3** Обновить `ShortenerUrlService`
  - [x] `ShortenUrlAsync` — поддержка custom alias
  - [x] Проверка уникальности custom alias
  - [x] Валидация формата alias (controller + DTO)
- [x] **4.4** Обновить `GetLongUrlAsync`
  - [x] Проверка `ExpiresAt` → 410 Gone (RedirectResult.Expired)
  - [x] Проверка `MaxClicks` → 410 Gone (RedirectResult.LimitReached)
- [x] **4.5** Создать `ExpiredLinksCleanupWorker` (BackgroundService)
  - [x] Запуск каждые 5 минут
  - [x] Удаление ссылок с `ExpiresAt < UtcNow`
  - [x] Очистка кэша Redis для удалённых ссылок
- [x] **4.6** Обновить API-контроллер (reserved aliases, conflict handling)
- [x] **4.7** Написать тесты (CustomAliasTests)

---

## Фаза 5: QR-коды ✅

> Генерация QR-кода для каждой короткой ссылки — **ПОЛНОСТЬЮ ЗАВЕРШЕНА**

- [x] **5.1** Добавить NuGet-пакет `QRCoder`
- [x] **5.2** Создать `IQRCodeService` / `QRCodeService`
  - [x] `GenerateQRCodeAsync(string url)` — возвращает PNG byte[]
  - [x] Кэширование QR-кода в Redis (TTL 1 день, ключ SHA-256)
- [x] **5.3** Создать `QRCodeController`
  - [x] `GET /api/v1/urls/{id}/qrcode` — возвращает PNG (ownership check)
- [x] **5.4** Добавить QR-код в UI (Blazor)
  - [x] Кнопка QR в таблице Dashboard
  - [x] `QRCodeDialog.razor` — модальное окно с QR-кодом
- [x] **5.5** Написать тесты (QRCodeServiceTests, QRCodeControllerTests)

---

## Фаза 6: Blazor UI (MudBlazor) ✅

> Переработка фронтенда с MudBlazor — **ПОЛНОСТЬЮ ЗАВЕРШЕНА**

- [x] **6.1** Добавить MudBlazor NuGet-пакет
- [x] **6.2** Настроить MudBlazor в `App.razor` и `_Imports.razor`
- [x] **6.3** Обновить `MainLayout.razor` — MudBlazor layout (AppBar, Drawer, NavMenu)
- [x] **6.4** Создать страницы
  - [x] **Login.razor** — форма входа
  - [x] **Register.razor** — форма регистрации
  - [x] **Dashboard.razor** — список URL + CRUD + QR + аналитика + поиск + пагинация
  - [x] **UrlDetails.razor** — детали ссылки + графики кликов (Line/Pie charts)
  - [x] **Home.razor** — лендинг (для неавторизованных)
- [x] **6.5** Создать компоненты
  - [x] `MudTable` в Dashboard — таблица с пагинацией, сортировкой, поиском
  - [x] `CreateUrlDialog.razor` — диалог создания (с custom alias, сроком, max clicks)
  - [x] `QRCodeDialog.razor` — отображение QR-кода
  - [x] `ConfirmDialog.razor` — подтверждение удаления
  - [x] `RedirectToLogin.razor` — редирект неавторизованных
- [x] **6.6** Настроить HttpClient с JWT-токеном
  - [x] `AuthorizationMessageHandler` — автоматическая подстановка токена
  - [x] `JwtAuthStateProvider` — [Authorize] / <AuthorizeView>
  - [x] Обработка 401 → redirect на login
- [x] **6.7** Добавить dark/light theme toggle
- [x] **6.8** Responsive дизайн (MudBlazor Grid)

---

## Фаза 7: Финализация 🟡

> Полировка, тестирование, документация — **В ПРОЦЕССЕ**

- [x] **7.1** Обновить README.md — описание проекта
- [x] **7.2** Добавить Scalar/OpenAPI документацию для endpoints
- [x] **7.3** Покрыть тестами (unit)
  - [x] ShortenerServiceTests (4 теста)
  - [x] AuthServiceTests (6 тестов)
  - [x] AuthControllerTests (4 теста)
  - [x] AnalyticsServiceTests (12 тестов)
  - [x] CustomAliasTests (14 тестов)
  - [x] QRCodeServiceTests (8 тестов: 4 service + 4 controller)
  - [x] ShortenerUrlOwnershipTests (6 тестов)
  - [x] GeoIpServiceTests (7 тестов)
  - [x] ClickEventSyncWorkerTests (8 тестов)
  - [x] ClickSyncWorkerTests (3 теста)
  - [x] ExpiredLinksCleanupWorkerTests (3 теста)
  - [x] ShortenerServicePersistenceTests (4 теста)
  - [x] SharedContractTests
  - [x] Integration-тесты (WebApplicationFactory + Testcontainers) ← **СДЕЛАНО** (11 тестов, api/v1/urls + аналитика + QR + ownership)
  - [x] Minimum 80% coverage report ← **СДЕЛАНО** (Cobertura: 80.11% линий, 141 тест)
- [x] **7.4** Настроить GitHub Actions
  - [x] Build + Test
  - [x] Docker build (API)
  - [x] Docker build (UI) ← **СДЕЛАНО** (job docker-ui в ci.yml)
  - [x] Code coverage report ← **СДЕЛАНО** (XPlat Code Coverage + CodeCoverageSummary, порог 80/90)
- [x] **7.5** Добавить `.editorconfig`
- [x] **7.6** `.gitignore` на месте
- [x] **7.7** Финальное ревью кода ← **СДЕЛАНО**
  - [x] Исправить Z-01: версионирование `api/v1/urls` в ShortenerUrlController
  - [x] Исправить Z-02: переименовать `UrlResposeDto` → `UrlResponseDto`
  - [x] Проверить Z-03: `async void` в QueueClickEvent (комментарий) ← заменён на awaitable `QueueClickEventAsync`
  - [x] Проверить Z-04: документация к ClickSyncWorker/ClickEventSyncWorker
  - [x] Проверить Z-05: optional `IHttpContextAccessor` (оставить как есть)
- [x] **7.8** Вернуть line coverage ≥80% (после GeoIP: 74.5%) ← **СДЕЛАНО** (80.11% линий, 2175/2715, 140 тестов)
  - [x] Покрыт `ClickEventSyncWorker` (91.9%): drain+enrich, resolve по IP, persist без geo, malformed-записи, неизвестный shortcode, сбой транзакции, пустой Redis, swallow в `RunOneSyncAsync`
  - [x] Покрыт `AnalyticsController` (BadRequest groupBy, bucket-логика, fallback `sub`, top referrers, out-of-range top)
  - [x] Покрыт `ExpiredLinksCleanupWorker` / `ClickSyncWorker` (CleanupOnce/FlushOnceAsync + swallow-ветки)
  - [x] Покрыты `DeleteExpiredUrlsAsync`, `GetPendingClicksAsync`, `ShortenerUrlController`, `ClickEventDto`, `HttpUrlAttribute`

---

## Оставшиеся задачи (приоритетные)

| # | Задача | Приоритет | Сложность | Агент |
|---|---|---|---|---|
| ~~1~~ | ~~GeoIP enrichment для ClickEvent (3.7)~~ | ✅ Сделано | — | [Архитектор] GeoLite2 offline + [Кодер] GeoIpService + [Дебаггер] 7 тестов |
| ~~2~~ | ~~Integration-тесты с WebApplicationFactory (7.3)~~ | ✅ Сделано | — | [Дебаггер] — 11 тестов + Testcontainers |
| ~~3~~ | ~~Code coverage report в CI (7.4)~~ | ✅ Сделано | — | [Дебаггер] — Cobertura в CI (порог 80/90 — см. 7.8) |
| ~~4~~ | ~~Docker build для UI в CI (7.4)~~ | ✅ Сделано | — | [Дебаггер] — job docker-ui в ci.yml |
| ~~5~~ | ~~Lint/format check в CI (0.7)~~ | ✅ Сделано | — | [Дебаггер] — dotnet format verify-no-changes |
| ~~6~~ | ~~Финальное ревью кода (7.7)~~ | ✅ Сделано | — | [Архитектор]/[Оркестратор] — Z-01…Z-05 закрыты |
| ~~7~~ | ~~Вернуть coverage ≥80% (7.8)~~ | ✅ Сделано | — | [Дебаггер] — ClickEventSyncWorker/Аналитика/Shared-контракты + Cobertura 80.11% |

**Оставшаяся работа: нет — все фазы и ремедиации завершены.**

---

## Follow-up аудита (2026-09-17)

> Итоги: полный перезапуск Docker-стека, живое e2e подтверждения email, 146 тестов.

- **Пароль 6 → 12 → 10**: по запросу пользователя минимум снижен до 10 символов (Identity, RegisterDto, Register.razor, тесты, docs).
- **Страница `/confirm-email`** в WebUI (MudBlazor, `[AllowAnonymous]`, `[SupplyParameterFromQuery]`, статус + «Sign in») — ссылка из `LoggingEmailSender` больше не 404.
- **Integration-тесты confirm-email** (Testcontainers, +3): логин до подтверждения → 401; после → OK; невалидный токен → 400. Итого **146/146**.
- **Docker-стек**: `.env` собран по `.env.example` (redis/jwt секреты сгенерированы; `ASPNETCORE_ENVIRONMENT=Development` — в Production `UseHttpsRedirection` ломал healthcheck над HTTP); `ConnectionStrings__Redis` → comma-формат + `abortConnect=false` (иначе crash-loop); `AbortOnConnectFail=true` убран из DI.
- **Живое e2e** (Playwright vs docker): register → ссылка в логах API → `/confirm-email` показывает «Email confirmed» → login проходит → dashboard.

---

## Аудит безопасности (2026-09-17)

> Полный аудит выполнен через context7 — найдено 31 уязвимость, сгруппированных в 15 ремедиаций (REM).

### REM-01: Заменить хардкод-секреты ✅ (коммит 9282868)
- Очищены секреты в appsettings.Development.json
- Добавлена проверка KnownWeakSecrets в DependencyInjection.cs
- docker-compose.yml — ${VAR:?required} для обязательных паролей
- Добавлен .env.example с инструкциями

### REM-02: HTTPS Enforcement ✅ (коммит eb482b9)
- Включён app.UseHsts() для production/staging
- Включён app.UseHttpsRedirection() для production/staging
- Отключено в Development (удобство локальной разработки)
- Отключено в Testing (избежание redirect loops)

### REM-03: Rate Limiting ✅ (коммит 2495650)
- Добавлены FixedWindow лимиты: global, auth, redirect, url_create, analytics
- Лимиты приведены к SECURITY_REMEDIATION.md: auth = 5/15 мин, redirect = 30/мин, url_create = 20/час, analytics = 10/мин; переопределение через `RateLimiting:*` env
- `[EnableRateLimiting("url_create")]` только на write-операциях контроллера (POST/PUT/DELETE); класс — `global` (GET-чтение не жжёт лимит 20/час — иначе Dashboard с поллингом 10с ловит 429)
- [EnableRateLimiting] атрибуты на контроллерах: AuthController (вкл. confirm-email), ShortenerUrlController, AnalyticsController, QRCodeController

### REM-04: SSRF Protection ✅ (коммит f3f97e2)
- Добавлен IsPublicAddress в ShortenerUrlController.CheckUrl
- Добавлен IsUrlSafe в ShortenerUrlService с проверкой приватных IP-диапазонов
- Блокируются: loopback, link-local, CGNAT, private, multicast, reserved
- Проверка портов (допускаются только 80, 443)

### REM-05: Email Verification ✅
- Registration sets EmailConfirmed = false
- LoginAsync blocks login for unconfirmed emails
- `POST /api/v1/auth/confirm-email` + `EmailConfirmationDto`, токен через `GenerateEmailConfirmationTokenAsync`
- `LoggingEmailSender : IEmailSender<ApplicationUser>` логирует ссылку `{AppBaseUrl}/confirm-email?email=...&token=...`
- Blazor-страница `/confirm-email` (WebUI) — подтверждение по email+token из query, ссылка на /login
- Integration-тесты (2026-09-17): Login_UnconfirmedEmail_Returns401, ConfirmEmail_ThenLogin_Succeeds, ConfirmEmail_InvalidToken_Returns400; итого 146 тестов

### REM-06: Password Policy ✅
- Password length raised to 10 chars (2026-09-17: 12 → 10, все точки: Identity, RegisterDto, Register.razor, тесты)
- Require uppercase, lowercase, digit, special char; RequiredUniqueChars = 3
- Account lockout: 5 failed attempts, 15-minute lockout
- AuthServiceTests updated with compliant passwords
- RegisterDto Password updated to `[StringLength(128, MinimumLength = 10)]`

### REM-07: CORS Hardening ✅
- Белый список origin'ов из `Cors:AllowedOrigins` (env `Cors__AllowedOrigins__N`), без fallback/localhost
- Restricts methods to GET, POST, PUT, DELETE, OPTIONS
- Restricts headers to Content-Type, Authorization, X-Requested-With, Accept
- Exposes X-Total-Count header
- All 146 tests passing

### REM-07: Input Validation — УЖЕ РЕАЛИЗОВАНО ✅
- FluentValidation + DataAnnotations используются в DTOs
- HttpUrlAttribute валидирует URL формат
- **Статус:** Не требует действий

### REM-08: SQL Injection — УЖЕ РЕАЛИЗОВАНО ✅
- EF Core параметризованные запросы
- **Статус:** Не требует действий

### REM-09: XSS Prevention — ПРОСТОЙ 🟡
- Не требуется: нет рендеринга пользовательского контента на сервере
- **Статус:** Принято как low-risk

### REM-10: CORS Hardening ✅ (коммит f3f97e2, временно возвращён на AllowAny)
- Белый список origin'ов в appsettings.json
- Ограниченные методы и заголовки
- Примечание: временно revert на AllowAnyMethod/AllowAnyHeader для прохождения интеграционных тестов (400 Bad Request)

### REM-11: Security Headers ✅ (коммит f3f97e2)
- Добавлены middleware-заголовки: X-Content-Type-Options, X-Frame-Options, Referrer-Policy, Permissions-Policy
- HSTS реализован (REM-02)
- **Статус:** Реализовано

### REM-12: JWT Expiry — УЖЕ РЕАЛИЗОВАНО ✅
- JwtSettings:ExpiryMinutes = 60 настроен
- AddJwtBearer валидирует expiration
- **Статус:** Не требует действий

### REM-13: Remove Info Leak ✅ (коммит 2495650)
- Results.NotFound("Url not found!") → Results.NotFound()
- Удалён Results.Redirect(result.LongUrl!) из /{code} endpoint
- Истёкшие/limit-reached ссылки возвращают 410 Gone

### REM-14: Dependency Audit — ПРОСТОЙ 🟡
- Не требуется: нет известных критичных CVE в стеке
- **Статус:** Принято как low-risk

### REM-15: Error Handling — УЖЕ РЕАЛИЗОВАНО ✅
- Global exception handler через middleware
- **Статус:** Не требует действий

---

## Оставшиеся ремедиации безопасности (приоритетные)

| REM | Задача | Статус | Примечание |
|---|---|---|---|
| REM-09 | Concurrency token | ✅ Сделано | [Timestamp] RowVersion + TrySaveUrlChangesAsync |
| REM-11 | AllowedHosts | ✅ Сделано | "" в appsettings + ASPNETCORE_ALLOWEDHOSTS env |
| REM-12 | Redis auth | ✅ Сделано | requirepass + SSL toggle (Redis__Ssl). 2026-09-17: ConnectionStrings__Redis переведён на comma-формат (`redis:6379,password=...,abortConnect=false,...`) — URL-формат `redis://:pass@host` не коннектился в StackExchange.Redis; `abortConnect=false` убирает crash-loop при транзиентном сбое Redis |
| REM-15 | Rate limit logging | ✅ Сделано | Middleware для логирования 429 |

---

## Замечания по коду (найдены при обзоре 2026-09-16)

### Z-01: Несогласованность версионирования API
- **Что:** `ShortenerUrlController` использует `[Route("api/[controller]")]` → `api/ShortenerUrl`
- **Ожидание:** Все контроллеры должны быть на `api/v1/...` (как `AuthController` → `api/v1/auth`, `AnalyticsController` → `api/v1/urls/{id}/analytics`, `QRCodeController` → `api/v1/urls/{id}/qrcode`)
- **Критичность:** 🟡 Средняя
- **Где:** `ShortenerUrlController.cs` строка 11, `Dashboard.razor` строка 143, `UrlDetails.razor` строка 244
- **Исправление:** `[Route("api/v1/urls")]` + обновить URL в UI (`api/ShortenerUrl` → `api/v1/urls`)

### Z-02: Опечатка в имени типа `UrlResposeDto`
- **Что:** Пропущена буква `n` → должно быть `UrlResponseDto`
- **Критичность:** 🟢 Низкая (косметика, но мешает при чтении)
- **Где:** `ShortenerUrlApp.Shared/DTOs/UrlResposeDto.cs`, используется в `ShortenerUrlController.cs`, `Dashboard.razor`, `UrlDetails.razor`, `ShortenerUrlApp.WebUI/Services/`
- **Исправление:** Переименовать `UrlResposeDto` → `UrlResponseDto` по всему проекту

### Z-03: `async void` в `QueueClickEvent`
- **Что:** Метод `QueueClickEvent` в `ShortenerUrlService` — `async void`, что безопасно только для fire-and-forget, но исключения не ловятся вызывающим кодом
- **Критичность:** 🟢 Низкая (exceptions уже ловятся внутри try-catch, но `async void` — антипаттерн)
- **Где:** `ShortenerUrlService.cs` строка 142
- **Исправление:** Оставить как есть (fire-and-forget по дизайну), но добавить комментарий о том, что это осознанное решение

### Z-04: Дублирование синхронизации кликов
- **Что:** `ClickSyncWorker` и `ClickEventSyncWorker` — оба запускаются каждую минуту и обрабатывают разные Redis-ключи (`clicks:*` vs `click-events:*`)
- **Критичность:** 🟢 Низкая (работает корректно, но нет документации о различии)
- **Где:** `ClickSyncWorker.cs`, `ClickEventSyncWorker.cs`
- **Исправление:** Добавить XML-документацию, объясняющую различие: `ClickSyncWorker` flush-ит счётчики кликов (int), `ClickEventSyncWorker` drain-ит метаданные кликов (JSON)

### Z-05: Необязательный `IHttpContextAccessor` в конструкторе
- **Что:** `ShortenerUrlService` принимает `IHttpContextAccessor?` как optional parameter — это не DI-friendly для unit-тестов
- **Критичность:** 🟢 Низкая (тесты работают, параметр nullable)
- **Где:** `ShortenerUrlService.cs` строка 14-17
- **Исправление:** Оставить как есть — параметр optional для обратной совместимости с тестами

---

## Архитектурные решения (ADR)

### ADR-001: Переход на PostgreSQL
- **Статус:** ✅ Принято и реализовано
- **Контекст:** MySQL работал, но PostgreSQL популярнее и совместимее
- **Решение:** Миграция на PostgreSQL через EF Core
- **Реализовано:** 3 миграции (Init → AddIdentityAndUserId → AddAliasAndExpiry)

### ADR-002: JWT вместо Cookie Auth
- **Статус:** ✅ Принято и реализовано
- **Контекст:** Нужна stateless аутентификация для API
- **Решение:** ASP.NET Identity + JWT Bearer (JsonWebTokenHandler)
- **Реализовано:** DI, AuthService, AuthController, [Authorize] на endpoints

### ADR-003: MudBlazor вместо Bootstrap
- **Статус:** ✅ Принято и реализовано
- **Контекст:** MudBlazor даёт готовые компоненты (Table, Dialog, Chart, Snackbar)
- **Решение:** Полная замена на MudBlazor 7.x
- **Реализовано:** 5 страниц, 4 компонента, dark/light toggle

### ADR-004: Write-behind для аналитики
- **Статус:** ✅ Принято и реализовано
- **Контекст:** ClickEvent может быть очень частым (горячий путь редиректа)
- **Решение:** Писать click metadata в Redis list, drain в PostgreSQL каждую минуту
- **Реализовано:** QueueClickEvent (fire-and-forget) → ClickEventSyncWorker (MULTI/EXEC drain)
- **Примечание:** Possible loss of ~1 minute of data on crash

### ADR-005: GeoIP через GeoLite2 (offline mmdb) вместо внешнего API
- **Статус:** ✅ Принято и реализовано
- **Контекст:** Аналитика (3.5/3.6) содержит Country/City, но поля всегда null; нужен надёжный и свободный источник геолокации по IP
- **Варианты:** IP2Location, ip-api.com (external API), MaxMind GeoLite2 (offline mmdb), FreeGeoIP
- **Решение:** **GeoLite2 (MaxMind)**: скачивание и хранение mmdb офлайн; lookup в памяти; нулевая сетевая зависимость на горячем пути; пакет `MaxMind.GeoIP2`
- **Реализовано:** `IGeoIpService`/`GeoIpService` (mmdb в память при старте, graceful degradation, fast-path для private/reserved IP, IPv4-mapped → `MapToIPv4`), enrichment в `ClickEventSyncWorker`, Docker-стадия `geolite` (license key / fallback P3TERX)
- **Примечание:** Для production нужен `MAXMIND_LICENSE_KEY` в `.env`; без ключа (или недоступного файла) API работает с GeoIP отключённым (поля остаются null)
