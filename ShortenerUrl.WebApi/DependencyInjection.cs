using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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
            // Lazy connect so EF Core design-time tools and app startup do not
            // require Redis to be reachable until the first cache access.
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString));

            services.AddCors(options => {
                options.AddDefaultPolicy(policy =>
                    policy.WithOrigins("https://localhost:7159")
                          .AllowAnyMethod()
                          .AllowAnyHeader());
            });

            // Identity without UI/cookies: UserManager + EF stores + token providers.
            services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequiredLength = 6;
                options.Password.RequireDigit = true;
                options.Password.RequireNonAlphanumeric = false;
                options.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<ShortenerUrlDbContext>()
            .AddDefaultTokenProviders();

            var jwtSection = configuration.GetSection("JwtSettings");
            var jwtSecret = jwtSection["Secret"];
            if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Length < 32)
            {
                throw new InvalidOperationException(
                    "JwtSettings:Secret is not configured. Set 'JwtSettings__Secret' with at least 32 characters.");
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
            services.AddHostedService<ClickSyncWorker>();

            return services;
        }

        public static void ApplyMigrations(this IApplicationBuilder app)
        {
            using var scope = app.ApplicationServices.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ShortenerUrlDbContext>();
            try
            {
                dbContext.Database.Migrate();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Migration error: {ex.Message}");
            }
        }
    }
}
