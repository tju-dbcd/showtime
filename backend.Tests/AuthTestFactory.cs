using System.Net.Http.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using ShowtimeBackend.Common;
using ShowtimeBackend.Data;
using ShowtimeBackend.Entities.UserPermission;

namespace ShowtimeBackend.Tests;

public sealed class AuthTestFactory : WebApplicationFactory<Program>
{
    public const string TestIssuer = "ShowtimeBackend.Tests";
    public const string TestAudience = "ShowtimeFrontend.Tests";
    public const string TestKey =
        "showtime-tests-only-jwt-key-which-is-longer-than-32-bytes";

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly FixedTimeProvider _timeProvider;
    private readonly string? _jwtKey;

    public AuthTestFactory(string? jwtKey = TestKey)
    {
        _jwtKey = jwtKey;
        UtcNow = DateTimeOffset.UtcNow;
        _timeProvider = new FixedTimeProvider(UtcNow);
        _connection.Open();
    }

    public DateTimeOffset UtcNow { get; }

    public HttpClient CreateApiClient() =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

    public async Task ResetDatabaseAsync()
    {
        await ExecuteDbContextAsync(async dbContext =>
        {
            await dbContext.Database.EnsureDeletedAsync();
            await dbContext.Database.EnsureCreatedAsync();
            return true;
        });
    }

    public Task<Role> SeedRoleAsync(
        string roleCode = "USER",
        bool status = true) =>
        ExecuteDbContextAsync(async dbContext =>
        {
            var role = new Role
            {
                RoleCode = roleCode,
                RoleName = $"{roleCode} role",
                Status = status,
                CreateBy = "tests",
                UpdateBy = "tests",
            };
            dbContext.Add(role);
            await dbContext.SaveChangesAsync();
            return role;
        });

    public Task<TResult> ExecuteDbContextAsync<TResult>(
        Func<AppDbContext, Task<TResult>> action)
    {
        return ExecuteAsync();

        async Task<TResult> ExecuteAsync()
        {
            await using var scope = Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await action(dbContext);
        }
    }

    public static async Task<ApiResponse<T>> ReadResponseAsync<T>(
        HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<ApiResponse<T>>()
        ?? throw new InvalidOperationException("The API response body was empty.");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // 测试环境不使用真实数据库（AppDbContext 在下方被替换为 SQLite），
                // 这里只提供合法的连接串占位，保证 Program.cs 的配置读取不会抛异常。
                ["ConnectionStrings:Oracle"] =
                    "User Id=tests;Password=tests;Data Source=localhost:1521/XEPDB1",
                ["Jwt:Key"] = _jwtKey,
                ["Jwt:Issuer"] = TestIssuer,
                ["Jwt:Audience"] = TestAudience,
                ["Jwt:ExpirationMinutes"] = "120",
                ["TicketSecurity:SigningKeyBase64"] =
                    "ERERERERERERERERERERERERERERERERERERERERERE=",
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<AppDbContext>();
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.RemoveAll<TimeProvider>();

            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.AddSingleton<TimeProvider>(_timeProvider);
            services.AddControllers()
                .AddApplicationPart(typeof(TestAuthorizationController).Assembly);
            services.AddDbContext<SqliteAuthDbContext>(options =>
                options.UseSqlite(_connection));
            services.AddScoped<AppDbContext>(provider =>
                provider.GetRequiredService<SqliteAuthDbContext>());
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
