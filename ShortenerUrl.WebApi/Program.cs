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

app.UseAuthorization();

app.MapControllers();

app.UseCors();

app.MapGet("/{code}", async (string code, IShortenerUrlService service, CancellationToken ct) =>
{
    // Метод сервиса сначала проверит Redis, что обеспечит высокую скорость
    var longUrl = await service.GetLongUrlAsync(code, ct);

    return string.IsNullOrEmpty(longUrl) ? Results.NotFound("Url not found!") : Results.Redirect(longUrl);
})
.WithName("RedirectToLongUrl");

app.ApplyMigrations();

app.Run();
