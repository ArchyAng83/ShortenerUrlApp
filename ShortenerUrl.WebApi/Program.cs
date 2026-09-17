using Scalar.AspNetCore;
using ShortenerUrlApp.WebApi;
using ShortenerUrlApp.WebApi.Services;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(opt =>
    {
        opt.Theme = ScalarTheme.DeepSpace;
        opt.DarkMode = true;
    });
}

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

// Security headers middleware
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.Append("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
    await next();
});

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
app.UseAuthorization();

app.MapControllers();

app.MapGet("/health", () => Results.Ok("Healthy")).WithName("Health");

app.MapGet("/{code}", async (string code, IShortenerUrlService service, CancellationToken ct) =>
{
    var result = await service.GetLongUrlWithStatusAsync(code, ct);

    if (result.IsExpired || result.IsLimitReached)
        return Results.StatusCode(StatusCodes.Status410Gone);

    return result.IsNotFound
        ? Results.NotFound()
        : Results.Redirect(result.LongUrl!);
})
.WithMetadata(new EnableRateLimitingAttribute("redirect"))
.WithName("RedirectToLongUrl");

app.ApplyMigrations();
app.Run();

public partial class Program { }
