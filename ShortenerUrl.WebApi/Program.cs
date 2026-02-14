using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using ShortenerUrlApp.WebApi.Data;

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

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

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
