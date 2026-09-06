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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShowtimeBackend.Common;
using ShowtimeBackend.Data;
using ShowtimeBackend.Entities.UserPermission;
using ShowtimeBackend.Services.FileStorage;
using ShowtimeBackend.Services.OrderTicket;
using ShowtimeBackend.Services.OrderTicket.Messaging;
using ShowtimeBackend.Services.UserPermission;

namespace ShowtimeBackend.Tests;

public sealed class AuthTestFactory : WebApplicationFactory<Program>
{
    public const string TestIssuer = "ShowtimeBackend.Tests";
    public const string TestAudience = "ShowtimeFrontend.Tests";
    public const string TestKey =
        "showtime-tests-only-jwt-key-which-is-longer-than-32-bytes";
    public const long TestSessionId = long.MaxValue;

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly FixedTimeProvider _timeProvider;
    private readonly string? _jwtKey;
    private readonly bool _ossEnabled;
    private readonly bool _replaceWithFakeStorage;
    private readonly IFileStorageService? _customFileStorage;
    private readonly bool _localStorageEnabled;
    private readonly string? _localStorageRoot;
    private readonly IReadOnlyDictionary<string, string?>? _additionalConfiguration;
    private readonly bool _enableOrderExpirationWorker;
    private readonly bool _enableOrderEventOutboxWorker;

    public AuthTestFactory(
        string? jwtKey = TestKey,
        bool ossEnabled = false,
        bool replaceWithFakeStorage = false,
        IFileStorageService? customFileStorage = null,
        bool localStorageEnabled = false,
        bool enableOrderExpirationWorker = false,
        bool enableOrderEventOutboxWorker = false,
        IReadOnlyDictionary<string, string?>? additionalConfiguration = null)
    {
        _jwtKey = jwtKey;
        _ossEnabled = ossEnabled;
        _replaceWithFakeStorage = replaceWithFakeStorage;
        _customFileStorage = customFileStorage;
        _localStorageEnabled = localStorageEnabled;
        _additionalConfiguration = additionalConfiguration;
        _enableOrderExpirationWorker = enableOrderExpirationWorker;
        _enableOrderEventOutboxWorker = enableOrderEventOutboxWorker;
        // 本地磁盘存储用例指向独立临时目录，测试结束整目录清理
        _localStorageRoot = localStorageEnabled
            ? Path.Combine(
                Path.GetTempPath(),
                "showtime-tests-files-" + Guid.NewGuid().ToString("N"))
            : null;
        _timeProvider = new FixedTimeProvider(DateTimeOffset.UtcNow);
        _connection.Open();
    }

    /// <summary>localStorageEnabled=true 时本地磁盘存储的根目录（测试临时目录），供用例断言落盘。</summary>
    public string? LocalStorageRoot => _localStorageRoot;

    public DateTimeOffset UtcNow => _timeProvider.GetUtcNow();

    public void AdvanceTime(TimeSpan amount) => _timeProvider.Advance(amount);

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
                ["Jwt:ExpirationMinutes"] = "15",
                ["Jwt:RefreshTokenExpirationDays"] = "7",
                ["RateLimiting:LoginPerMinute"] = "5",
                ["RateLimiting:RegisterPerMinute"] = "3",
                ["RateLimiting:RefreshPerMinute"] = "10",
                ["RateLimiting:AuthenticatedPerMinute"] = "120",
                ["RateLimiting:AnonymousPerMinute"] = "60",
                ["TicketSecurity:SigningKeyBase64"] =
                    "ERERERERERERERERERERERERERERERERERERERERERE=",
                ["IdentityData:EncryptionKey"] =
                    "QkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkI=",
                // 测试环境默认不启用 OSS（kill-switch 关闭，跳过启动期配置校验）；
                // ossEnabled=true 时给出合法占位配置（AccessKey 由测试注入 fake 或在校验前短路，不真连 OSS）。
                ["Oss:Enabled"] = _ossEnabled ? "true" : "false",
                ["Oss:Endpoint"] = "https://oss-cn-hangzhou.aliyuncs.com",
                // 非空测试密钥：满足启动校验且 OssClient 可构造；
                // 校验类用例在触网前短路，成功路径用 fake 覆盖，不会真连 OSS。
                ["Oss:AccessKeyId"] = "test-access-key-id",
                ["Oss:AccessKeySecret"] = "test-access-key-secret",
                ["Oss:Bucket"] = "showtime-assets",
                ["Oss:BaseUrl"] = "https://showtime-assets.oss-cn-hangzhou.aliyuncs.com",
                // 测试用小体积上限，超限用例无需真的发 5MB
                ["Oss:MaxFileSizeBytes"] = "2048",
                // 测试默认不启用本地磁盘存储（与"未配置即 503"语义一致）；
                // localStorageEnabled=true 的用例指向独立临时目录并公开托管 BaseUrl
                ["LocalStorage:Enabled"] = _localStorageEnabled ? "true" : "false",
                ["LocalStorage:RootDirectory"] =
                    _localStorageEnabled ? _localStorageRoot! : Path.GetTempPath(),
                ["LocalStorage:BaseUrl"] = "/files",
            });
            if (_additionalConfiguration is not null)
            {
                configuration.AddInMemoryCollection(_additionalConfiguration);
            }
        });
        builder.ConfigureServices(services =>
        {
            if (!_enableOrderExpirationWorker)
            {
                var workerDescriptors = services
                    .Where(descriptor =>
                        descriptor.ServiceType == typeof(IHostedService) &&
                        descriptor.ImplementationType == typeof(OrderExpirationWorker))
                    .ToList();
                foreach (var descriptor in workerDescriptors)
                    services.Remove(descriptor);
            }

            // PR65 后 outbox worker 默认始终注册（RabbitMQ 关闭时进程内完成退款/通知）；
            // 绝大多数用例并不需要后台轮询，默认移除以免后台 worker 变更库状态造成竞态，
            // 需要验证兜底链路/注册的用例显式开启。
            if (!_enableOrderEventOutboxWorker)
            {
                var outboxWorkerDescriptors = services
                    .Where(descriptor =>
                        descriptor.ServiceType == typeof(IHostedService) &&
                        descriptor.ImplementationType == typeof(OrderEventOutboxWorker))
                    .ToList();
                foreach (var descriptor in outboxWorkerDescriptors)
                    services.Remove(descriptor);
            }

            services.RemoveAll<AppDbContext>();
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.RemoveAll<TimeProvider>();
            services.RemoveAll<IUserSessionService>();

            if (_customFileStorage is not null)
            {
                // 指定注入的自定义实现（如模拟 OSS 故障的测试 double）优先
                services.RemoveAll<IFileStorageService>();
                services.AddSingleton(_customFileStorage);
            }
            else if (_ossEnabled && _replaceWithFakeStorage)
            {
                // 上传全链路（含成功路径）测试：注入内存 fake，不依赖真实 OSS
                services.RemoveAll<IFileStorageService>();
                services.AddSingleton<IFileStorageService, FakeFileStorageService>();
            }

            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.AddSingleton<TimeProvider>(_timeProvider);
            services.AddControllers()
                .AddApplicationPart(typeof(TestAuthorizationController).Assembly);
            services.AddDbContext<SqliteAuthDbContext>((provider, options) =>
                options
                    .UseSqlite(_connection)
                    .AddInterceptors(
                        provider.GetRequiredService<
                            ShowtimeBackend.Data.Interceptors.UserRealNameEncryptionInterceptor>()));
            services.AddScoped<AppDbContext>(provider =>
                provider.GetRequiredService<SqliteAuthDbContext>());
            // 审计 sink 使用独立 DbContext 实例写入 OPERATION_LOG：
            // 表结构由 SqliteAuthDbContext.EnsureCreated 建立，factory 仅负责出实例做 INSERT。
            services.AddDbContextFactory<AppDbContext>(options =>
                options.UseSqlite(_connection));
            services.AddScoped<UserSessionService>();
            services.AddScoped<IUserSessionService>(provider =>
                new TestUserSessionService(
                    provider.GetRequiredService<UserSessionService>()));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
            if (_localStorageRoot is not null)
            {
                try
                {
                    Directory.Delete(_localStorageRoot, recursive: true);
                }
                catch (IOException)
                {
                    // 测试临时文件清理失败不影响用例结论
                }
                catch (UnauthorizedAccessException)
                {
                    // 同上：尽力清理
                }
            }
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private long _utcTicks = utcNow.UtcTicks;

        public override DateTimeOffset GetUtcNow() =>
            new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);

        public void Advance(TimeSpan amount) =>
            Interlocked.Add(ref _utcTicks, amount.Ticks);
    }

    private sealed class TestUserSessionService(
        UserSessionService inner) : IUserSessionService
    {
        public Task<UserSessionResult<SessionIssueData>> StartAsync(
            long userId,
            ClientRequestMetadata client,
            CancellationToken cancellationToken) =>
            inner.StartAsync(userId, client, cancellationToken);

        public Task<UserSessionResult<SessionRefreshData>> RotateAsync(
            string refreshToken,
            CancellationToken cancellationToken) =>
            inner.RotateAsync(refreshToken, cancellationToken);

        public Task<bool> IsActiveAsync(
            long userId,
            long sessionId,
            CancellationToken cancellationToken) =>
            sessionId == TestSessionId
                ? Task.FromResult(true)
                : inner.IsActiveAsync(userId, sessionId, cancellationToken);

        public Task<int> LogoutCurrentAsync(
            long userId,
            long sessionId,
            CancellationToken cancellationToken) =>
            inner.LogoutCurrentAsync(userId, sessionId, cancellationToken);

        public Task<int> LogoutAllAsync(
            long userId,
            CancellationToken cancellationToken) =>
            inner.LogoutAllAsync(userId, cancellationToken);

        public Task<IReadOnlyList<ShowtimeBackend.DTOs.UserPermission.UserSessionResponse>> ListAsync(
            long userId,
            long currentSessionId,
            CancellationToken cancellationToken) =>
            inner.ListAsync(userId, currentSessionId, cancellationToken);

        public Task<UserSessionResult<int>> RevokeAsync(
            long userId,
            long targetSessionId,
            CancellationToken cancellationToken) =>
            inner.RevokeAsync(userId, targetSessionId, cancellationToken);
    }
}
