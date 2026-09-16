using Scalar.AspNetCore;
using ShortenerUrlApp.WebApi;
using ShortenerUrlApp.WebApi.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(opt =>
    {
        opt.Theme = ScalarTheme.DeepSpace;
        opt.DarkMode = true;
    });
}

//app.UseHttpsRedirection();

// CORS before auth so preflight OPTIONS requests are not rejected with 401.
app.UseCors();

// Authentication must run before authorization.
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Used by the docker-compose health check; the literal segment wins over the /{code} redirect route.
app.MapGet("/health", () => Results.Ok("Healthy")).WithName("Health");

app.MapGet("/{code}", async (string code, IShortenerUrlService service, CancellationToken ct) =>
{
    // Метод сервиса сначала проверит Redis, что обеспечит высокую скорость.
    // Статус-результат отличает несуществующую ссылку (404) от истёкшей или
    // достигшей лимита кликов (410 Gone).
    var result = await service.GetLongUrlWithStatusAsync(code, ct);

    if (result.IsExpired || result.IsLimitReached)
        return Results.StatusCode(StatusCodes.Status410Gone);

    return result.IsNotFound
        ? Results.NotFound("Url not found!")
        : Results.Redirect(result.LongUrl!);
})
.WithName("RedirectToLongUrl");

app.ApplyMigrations();

app.Run();

// Exposed so integration tests (WebApplicationFactory) and tooling can reference the app entry point.
public partial class Program { }
