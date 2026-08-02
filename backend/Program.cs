using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ShowtimeBackend.Common;
using ShowtimeBackend.Common.Jwt;
using ShowtimeBackend.Common.OpenApi;
using ShowtimeBackend.Data;
using ShowtimeBackend.Entities.UserPermission;
using ShowtimeBackend.Services.Auth;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>((serviceProvider, options) =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var userId = configuration["Oracle_UserId"]
        ?? throw new InvalidOperationException(
            "Environment variable 'Oracle_UserId' is not set.");
    var password = configuration["Oracle_Password"]
        ?? throw new InvalidOperationException(
            "Environment variable 'Oracle_Password' is not set.");
    var connectionString =
        $"User Id={userId};Password={password};Data Source=120.27.157.163:1521/XEPDB1";

    options.UseOracle(connectionString);
});

builder.Services
    .AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(
        options => Encoding.UTF8.GetByteCount(options.Key ?? string.Empty) >= 32,
        "Jwt:Key must contain at least 32 UTF-8 bytes.")
    .ValidateOnStart();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();
builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<Microsoft.Extensions.Options.IOptions<JwtOptions>>(
        (bearerOptions, jwtOptionsAccessor) =>
        {
            var jwtOptions = jwtOptionsAccessor.Value;
            bearerOptions.MapInboundClaims = false;
            bearerOptions.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwtOptions.Issuer,
                ValidateAudience = true,
                ValidAudience = jwtOptions.Audience,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(jwtOptions.Key)),
                ClockSkew = TimeSpan.Zero,
                NameClaimType = JwtRegisteredClaimNames.UniqueName,
                RoleClaimType = "role",
            };
        });
builder.Services.AddAuthorization();

builder.Services
    .AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var message = string.Join(
                " ",
                context.ModelState.Values
                    .SelectMany(entry => entry.Errors)
                    .Select(error => error.ErrorMessage)
                    .Where(error => !string.IsNullOrWhiteSpace(error)));

            if (string.IsNullOrWhiteSpace(message))
            {
                message = "The request is invalid.";
            }

            return new BadRequestObjectResult(
                ApiResponse<object>.Fail(
                    "AUTH_VALIDATION_FAILED",
                    message));
        };
    });

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IPasswordHasher<SysUser>, PasswordHasher<SysUser>>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();

builder.Services.AddOpenApi(options =>
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>());

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => "Showtime API is running.");
app.MapControllers();

app.Run();

public partial class Program;
