using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Data;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Oracle connection string from environment variables
var userId = Environment.GetEnvironmentVariable("Oracle_UserId")
    ?? throw new InvalidOperationException("Environment variable 'Oracle_UserId' is not set.");
var password = Environment.GetEnvironmentVariable("Oracle_Password")
    ?? throw new InvalidOperationException("Environment variable 'Oracle_Password' is not set.");
var connectionString = $"User Id={userId};Password={password};Data Source=120.27.157.163:1521/XEPDB1";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseOracle(connectionString));

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.MapGet("/", () => "Showtime API is running.");

app.Run();
