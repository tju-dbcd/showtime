using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ShowtimeBackend.Common;
using ShowtimeBackend.Common.Jwt;
using ShowtimeBackend.Common.Middlewares;
using ShowtimeBackend.Common.OpenApi;
using ShowtimeBackend.Common.Oss;
using ShowtimeBackend.Common.TicketSecurity;
using ShowtimeBackend.Data;
using ShowtimeBackend.Entities.UserPermission;
using ShowtimeBackend.Services.UserPermission;
using ShowtimeBackend.Services.FileStorage;
using ShowtimeBackend.Services.OrderTicket;
using ShowtimeBackend.Services.ShowSession;
using ShowtimeBackend.Services.Impl;
using ShowtimeBackend.Services.SeatZone;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using StackExchange.Redis;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog：结构化日志（控制台 + 文件滚动），为里程碑 5 的 Loki+Grafana 日志采集铺路；
// 等级/输出/模板全部来自 appsettings 的 Serilog 节，生产可无代码调整。
builder.Host.UseSerilog((context, services, configuration) =>
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

// Oracle 连接配置单一来源：业务上下文用 scoped 桥接（每请求作用域共享一个实例，语义与 AddDbContext 一致）；
// 审计 sink（DbOperationTicketAuditSink）经 IDbContextFactory 创建独立实例，保证审计写入不卷入业务事务。
// 注意：不得再对 AppDbContext 调用 AddDbContext，否则其注册的 scoped DbContextOptions 会与
// singleton DbContextFactory 冲突（Cannot consume scoped service from singleton）。
Action<DbContextOptionsBuilder> configureDatabase = options => options.UseOracle(
    builder.Configuration.GetConnectionString("Oracle")
    ?? throw new InvalidOperationException(
        "Connection string 'Oracle' is not set."));
builder.Services.AddDbContextFactory<AppDbContext>(configureDatabase);
builder.Services.AddScoped<AppDbContext>(provider =>
    provider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());

// Redis：懒连接注册，启动不阻塞；Redis 未启动时应用照常启动，选座锁守卫自动降级为纯 Oracle 流程。
var redisConnectionString = builder.Configuration.GetConnectionString("Redis")
    ?? "localhost:6379,abortConnect=false,connectRetry=3,connectTimeout=3000";
builder.Services.AddStackExchangeRedisCache(options =>
{
    // 常规缓存（场次/演出热点缓存等里程碑 5 场景直接使用 IDistributedCache）
    options.Configuration = redisConnectionString;
    options.InstanceName = "showtime:";
});
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(ConfigurationOptions.Parse(redisConnectionString)));
builder.Services.AddSingleton<ISeatLockGuard, RedisSeatLockGuard>();

// OSS 文件存储配置：启动即校验（仅 Oss:Enabled=true 时要求 Endpoint/Bucket/BaseUrl）；
// AccessKeyId/Secret 走环境变量 Oss__AccessKeyId / Oss__AccessKeySecret 注入，不落仓库。
builder.Services
    .AddOptions<OssOptions>()
    .Bind(builder.Configuration.GetSection(OssOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<
    Microsoft.Extensions.Options.IValidateOptions<OssOptions>,
    OssOptionsValidator>();
// 文件存储服务：Oss:Enabled=false（kill-switch，如本地无 OSS 环境）时用内存 fake 继续开发；
// 启用后走真实 OSS 实现（AccessKey 只存在后端，代理上传）。
// 注意：通过已解析的 IOptions<OssOptions> 懒判断，而非配置的即时读取——
// 这样测试/运行时注入的 Oss:Enabled 覆盖才生效（即时读取会错过后置配置源）。
builder.Services.AddSingleton<OssFileStorageService>();
builder.Services.AddSingleton<FakeFileStorageService>();
builder.Services.AddSingleton<IFileStorageService>(serviceProvider =>
{
    var ossEnabled = serviceProvider
        .GetRequiredService<IOptions<OssOptions>>()
        .Value
        .Enabled;
    return ossEnabled
        ? serviceProvider.GetRequiredService<OssFileStorageService>()
        : serviceProvider.GetRequiredService<FakeFileStorageService>();
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
    .AddOptions<TicketSecurityOptions>()
    .Bind(builder.Configuration.GetSection(TicketSecurityOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<
    Microsoft.Extensions.Options.IValidateOptions<TicketSecurityOptions>,
    TicketSecurityOptionsValidator>();

builder.Services
    .AddOptions<TicketRedemptionOptions>()
    .Bind(builder.Configuration.GetSection(TicketRedemptionOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // 统一 401/403 响应体为 ApiResponse 信封（与业务错误格式一致）
        JwtErrorEnvelope.Configure(options.Events);
    });
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
    .AddJsonOptions(options =>
    {
        // 枚举序列化为成员名字符串（与数据库 CHECK 约束取值一致），
        // 并使 OpenAPI 生成 enum 约束进入 schema。
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter(allowIntegerValues: false));
    })
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
                    "VALIDATION_FAILED",
                    message));
        };
    });

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IPasswordHasher<SysUser>, PasswordHasher<SysUser>>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddSingleton<ITicketTokenService, HmacTicketTokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<ITicketIssuanceService, TicketIssuanceService>();
builder.Services.AddScoped<ITicketQueryService, TicketQueryService>();
builder.Services.AddScoped<ITicketRedemptionService, TicketRedemptionService>();
builder.Services.AddScoped<IAdminTicketIssuanceService, AdminTicketIssuanceService>();
builder.Services.AddScoped<IRefundPolicyAdminService, RefundPolicyAdminService>();
builder.Services.AddSingleton<RefundPolicyEngine>();
builder.Services.AddScoped<IRefundLockCoordinator, OracleRefundLockCoordinator>();
builder.Services.AddScoped<IRefundApplicationService, RefundApplicationService>();
builder.Services.AddScoped<IRefundReviewService, RefundReviewService>();
builder.Services.AddScoped<IOrderTicketAuditSink, DbOperationTicketAuditSink>();
builder.Services.AddScoped<IClientShowSessionService, ShowSessionService>();
builder.Services.AddScoped<IAdminShowSessionService, AdminShowSessionService>();
builder.Services.AddScoped<ISeatLockService>(serviceProvider =>
    new SeatLockService(
        serviceProvider.GetRequiredService<AppDbContext>(),
        serviceProvider.GetRequiredService<TimeProvider>(),
        serviceProvider.GetRequiredService<ISeatLockGuard>(),
        // Redis:SeatLockGuardEnabled 为 kill-switch，false 时完全走纯 Oracle 流程
        serviceProvider.GetRequiredService<IConfiguration>()
            .GetValue("Redis:SeatLockGuardEnabled", true)));
builder.Services.AddScoped<IAdminShowService, AdminShowService>();
builder.Services.AddScoped<IClientShowService, ClientShowService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiResponseExceptionHandler>();
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
    options.AddSchemaTransformer<EnumStringSchemaTransformer>();
    options.AddSchemaTransformer<TicketRedemptionSchemaTransformer>();
});

var app = builder.Build();

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => "Showtime API is running.");
app.MapControllers();

app.Run();

public partial class Program;
