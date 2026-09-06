using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using ShowtimeBackend.Common;

namespace ShowtimeBackend.Common.RateLimiting;

public static class RateLimitingServiceExtensions
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public static IServiceCollection AddApiRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<ApiRateLimitOptions>()
            .Bind(configuration.GetSection(ApiRateLimitOptions.SectionName))
            .Validate(options => options.IsValid(),
                "All RateLimiting permit values must be positive integers.")
            .ValidateOnStart();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(
                ApiRateLimitPolicyNames.Login,
                context => CreateIpPartition(
                    context,
                    GetConfiguredOptions(context).LoginPerMinute));
            options.AddPolicy(
                ApiRateLimitPolicyNames.Register,
                context => CreateIpPartition(
                    context,
                    GetConfiguredOptions(context).RegisterPerMinute));
            options.AddPolicy(
                ApiRateLimitPolicyNames.Refresh,
                context => CreateIpPartition(
                    context,
                    GetConfiguredOptions(context).RefreshPerMinute));

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                context =>
                {
                    var dedicatedPolicy = context.GetEndpoint()?.Metadata
                        .GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
                    if (ApiRateLimitPolicyNames.IsDedicated(dedicatedPolicy))
                    {
                        return RateLimitPartition.GetNoLimiter(
                            $"dedicated:{dedicatedPolicy}");
                    }

                    var configured = GetConfiguredOptions(context);
                    var userId = context.User.FindFirst(
                        JwtRegisteredClaimNames.Sub)?.Value;
                    var authenticated = context.User.Identity?.IsAuthenticated == true
                        && !string.IsNullOrWhiteSpace(userId);
                    var partitionKey = authenticated
                        ? $"user:{userId}"
                        : $"ip:{GetConnectionKey(context)}";
                    var permitLimit = authenticated
                        ? configured.AuthenticatedPerMinute
                        : configured.AnonymousPerMinute;
                    return CreateFixedWindowPartition(partitionKey, permitLimit);
                });

            options.OnRejected = async (context, cancellationToken) =>
            {
                var retryAfter = Window;
                if (context.Lease.TryGetMetadata(
                        MetadataName.RetryAfter,
                        out var leaseRetryAfter))
                {
                    retryAfter = leaseRetryAfter;
                }

                var retryAfterSeconds = Math.Max(
                    1,
                    (long)Math.Ceiling(retryAfter.TotalSeconds));
                context.HttpContext.Response.StatusCode =
                    StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.Headers.RetryAfter =
                    retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
                await context.HttpContext.Response.WriteAsJsonAsync(
                    ApiResponse<object>.Fail(
                        "RATE_LIMIT_EXCEEDED",
                        "Too many requests. Please retry later."),
                    cancellationToken);
            };
        });

        return services;
    }

    private static ApiRateLimitOptions GetConfiguredOptions(
        HttpContext context) =>
        context.RequestServices
            .GetRequiredService<IOptions<ApiRateLimitOptions>>()
            .Value;

    private static RateLimitPartition<string> CreateIpPartition(
        HttpContext context,
        int permitLimit) =>
        CreateFixedWindowPartition(
            $"ip:{GetConnectionKey(context)}",
            permitLimit);

    private static RateLimitPartition<string> CreateFixedWindowPartition(
        string partitionKey,
        int permitLimit) =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = permitLimit,
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                Window = Window,
            });

    internal static string GetConnectionKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString()
        ?? $"connection:{context.Connection.Id}";
}
