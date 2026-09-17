using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using ShortenerUrlApp.WebApi.Data;
using ShortenerUrlApp.WebApi.Entities;
using ShortenerUrlApp.WebApi.Services;
using StackExchange.Redis;
using System.Text;

namespace ShortenerUrlApp.WebApi
{
    //Перенесено в DI для чистоты в Program.cs
    public static class DependencyInjection
    {
        private static readonly HashSet<string> KnownWeakSecrets = new()
        {
            "your-super-secret-key-minimum-32-characters-long",
            "your_secure_password",
            "password",
            "secret",
            "12345678901234567890123456789012"
        };

        public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            // Connection strings are externalized: base values are empty defaults,
            // overridden via ConnectionStrings__DefaultConnection / ConnectionStrings__Redis env vars.
            var connection = configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connection))
            {
                throw new InvalidOperationException(
                    "Database connection string is not configured. Set 'ConnectionStrings__DefaultConnection' or configure appsettings.");
            }
            services.AddDbContext<ShortenerUrlDbContext>(opt => opt.UseNpgsql(connection));

            var redisConnectionString = configuration.GetConnectionString("Redis");
            if (string.IsNullOrWhiteSpace(redisConnectionString))
            {
                throw new InvalidOperationException(
                    "Redis connection string is not configured. Set 'ConnectionStrings__Redis' or configure appsettings.");
            }
            services.AddSingleton<IConnectionMultiplexer>(_ =>
            {
                var config = ConfigurationOptions.Parse(redisConnectionString);
                config.Ssl = configuration.GetValue<bool>("Redis__Ssl");
                // AbortOnConnectFail is governed by the connection string (abortConnect=...):
                // docker-compose sets it to false so a transient Redis blip fails the worker
                // run instead of taking the whole host down (BackgroundServiceExceptionBehavior).
                return ConnectionMultiplexer.Connect(config);
            });

            // Allowed browser origins come from configuration ('Cors:AllowedOrigins' in appsettings
            // or Cors__AllowedOrigins__N env vars); no hardcoded fallback — CORS is effectively
            // disabled when nothing is configured.
            var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                is { Length: > 0 } configured
                    ? configured
                    : [];

            services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    policy.WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
                          .WithHeaders("Content-Type", "Authorization", "X-Requested-With", "Accept")
                          .WithExposedHeaders("X-Total-Count");

                    if (allowedOrigins.Length > 0)
                    {
                        policy.WithOrigins(allowedOrigins);
                    }
                });
            });

            // Rate limiting policies
            // Defaults match the security plan; override via 'RateLimiting:*' env vars
            // (e.g. RateLimiting__AuthPermitLimit=1000 in a test/docker environment).
            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                int PermitLimit(string key, int fallback) =>
                    configuration.GetValue<int?>($"RateLimiting:{key}") ?? fallback;

                options.AddFixedWindowLimiter("global", limiter =>
                {
                    limiter.PermitLimit = PermitLimit("GlobalPermitLimit", 100);
                    limiter.Window = TimeSpan.FromMinutes(1);
                    limiter.QueueLimit = 10;
                });

                // Brute-force protection: 5 auth attempts per 15 minutes per client.
                options.AddFixedWindowLimiter("auth", limiter =>
                {
                    limiter.PermitLimit = PermitLimit("AuthPermitLimit", 5);
                    limiter.Window = TimeSpan.FromMinutes(15);
                    limiter.QueueLimit = 0;
                });

                options.AddFixedWindowLimiter("redirect", limiter =>
                {
                    limiter.PermitLimit = PermitLimit("RedirectPermitLimit", 30);
                    limiter.Window = TimeSpan.FromMinutes(1);
                });

                options.AddFixedWindowLimiter("url_create", limiter =>
                {
                    limiter.PermitLimit = PermitLimit("UrlCreatePermitLimit", 20);
                    limiter.Window = TimeSpan.FromHours(1);
                });

                options.AddFixedWindowLimiter("analytics", limiter =>
                {
                    limiter.PermitLimit = PermitLimit("AnalyticsPermitLimit", 10);
                    limiter.Window = TimeSpan.FromMinutes(1);
                });
            });

            // Identity without UI/cookies: UserManager + EF stores + token providers.
            services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequiredLength = 10;
                options.Password.RequireDigit = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequiredUniqueChars = 3;
                options.User.RequireUniqueEmail = true;

                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddEntityFrameworkStores<ShortenerUrlDbContext>()
            .AddDefaultTokenProviders();

            // Logs confirmation/reset links until a real SMTP-backed sender is configured.
            services.AddScoped<IEmailSender<ApplicationUser>, LoggingEmailSender>();

            var jwtSection = configuration.GetSection("JwtSettings");
            var jwtSecret = jwtSection["Secret"];
            if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Length < 32
                || KnownWeakSecrets.Contains(jwtSecret))
            {
                throw new InvalidOperationException(
                    "JwtSettings:Secret is not configured or is a known-weak value. Set 'JwtSettings__Secret' with at least 32 cryptographically random characters.");
            }

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = jwtSection["Issuer"],
                        ValidateAudience = true,
                        ValidAudience = jwtSection["Audience"],
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
                        ValidateLifetime = true,
                        ClockSkew = TimeSpan.Zero
                    };
                });

            services.AddScoped<IShortenerUrlService, ShortenerUrlService>();
            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<IAnalyticsService, AnalyticsService>();
            services.AddScoped<IQRCodeService, QRCodeService>();
            services.AddSingleton<IGeoIpService, GeoIpService>();

            // Exposes the current request (IP / User-Agent / Referer) to ShortenerUrlService
            // so it can buffer click metadata on the redirect path.
            services.AddHttpContextAccessor();

            services.AddHostedService<ClickSyncWorker>();
            services.AddHostedService<ExpiredLinksCleanupWorker>();
            services.AddHostedService<ClickEventSyncWorker>();

            return services;
        }

        public static void ApplyMigrations(this IApplicationBuilder app)
        {
            using var scope = app.ApplicationServices.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ShortenerUrlDbContext>();
            var logger = scope.ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger("Migrations");
            try
            {
                dbContext.Database.Migrate();
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Migration error");
            }
        }
    }
}
