using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using ShortenerUrlApp.WebApi.Data;
using ShortenerUrlApp.WebApi.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var connection = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ShortenerUrlDbContext>(opt =>
{
    opt.UseMySQL(connection!);
});

builder.Services.AddCors(options => {
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("https://localhost:7159")
         .AllowAnyMethod()
         .AllowAnyHeader());
});

builder.Services.AddScoped<IShortenerUrlService, ShortenerUrlService>();

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

    if (string.IsNullOrEmpty(longUrl))
        return Results.NotFound("Url not found!");

    // 302 Redirect — браузер перейдет по адресу, а мы засчитаем клик
    return Results.Redirect(longUrl);
})
.WithName("RedirectToLongUrl");

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ShortenerUrlDbContext>();

	try
	{
		dbContext.Database.Migrate();
	}
	catch (Exception ex)
	{

        Console.WriteLine($"Connection not found! {ex.Message}");
	}
}

app.Run();
