using Microsoft.EntityFrameworkCore;
using ShortenerUrlApp.WebApi.Data;
using ShortenerUrlApp.WebApi.Services;
using StackExchange.Redis;

namespace ShortenerUrlApp.WebApi
{
    //Перенесено в DI для чистоты в Program.cs
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            // База данных
            var connection = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Database connection not found.");
            services.AddDbContext<ShortenerUrlDbContext>(opt => opt.UseMySQL(connection));

            // Redis
            var redisConnectionString = configuration.GetConnectionString("Redis")
                ?? throw new InvalidOperationException("Redis connection not found.");
            services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConnectionString));

            // CORS
            services.AddCors(options => {
                options.AddDefaultPolicy(policy =>
                    policy.WithOrigins("https://localhost:7159") // Твой порт UI
                          .AllowAnyMethod()
                          .AllowAnyHeader());
            });

            // Сервисы бизнес-логики
            services.AddScoped<IShortenerUrlService, ShortenerUrlService>();
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
