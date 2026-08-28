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
using ShowtimeBackend.Common.TicketSecurity;
using ShowtimeBackend.Data;
using ShowtimeBackend.Entities.UserPermission;
using ShowtimeBackend.Services.UserPermission;
using ShowtimeBackend.Services.OrderTicket;
using ShowtimeBackend.Services.ShowSession;
using ShowtimeBackend.Services.Impl;
using ShowtimeBackend.Services.SeatZone;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>((serviceProvider, options) =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    options.UseOracle(
        configuration.GetConnectionString("Oracle")
        ?? throw new InvalidOperationException(
            "Connection string 'Oracle' is not set."));
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
builder.Services.AddSingleton<IOrderTicketAuditSink, NullOrderTicketAuditSink>();
builder.Services.AddScoped<IClientShowSessionService, ShowSessionService>();
builder.Services.AddScoped<IAdminShowSessionService, AdminShowSessionService>();
builder.Services.AddScoped<ISeatLockService, SeatLockService>();
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
