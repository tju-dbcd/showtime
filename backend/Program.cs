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
using Scalar.AspNetCore;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>((serviceProvider, options) =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    options.UseOracle(
        configuration.GetConnectionString("Oracle")
        ?? throw new InvalidOperationException(
            "Connection string 'Oracle' is not set."));
});

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
// 文件存储服务：M1 先用内存 fake 打通骨架；M2 替换为 OssFileStorageService（真实 OSS 实现）。
builder.Services.AddSingleton<IFileStorageService, FakeFileStorageService>();

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
builder.Services.AddScoped<IAdminTicketIssuanceService, AdminTicketIssuanceService>();
builder.Services.AddScoped<IRefundPolicyAdminService, RefundPolicyAdminService>();
builder.Services.AddSingleton<RefundPolicyEngine>();
builder.Services.AddScoped<IRefundLockCoordinator, OracleRefundLockCoordinator>();
builder.Services.AddScoped<IRefundApplicationService, RefundApplicationService>();
builder.Services.AddScoped<IRefundReviewService, RefundReviewService>();
builder.Services.AddSingleton<IOrderTicketAuditSink, NullOrderTicketAuditSink>();
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
