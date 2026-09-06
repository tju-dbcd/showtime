using System.Text.Json;
using ShowtimeBackend.Data;
using ShowtimeBackend.Entities.UserPermission;

namespace ShowtimeBackend.Services.UserPermission;

public sealed class DatabaseOperationLogWriter(
    IServiceScopeFactory scopeFactory,
    IHttpContextAccessor httpContextAccessor,
    TimeProvider timeProvider,
    ILogger<DatabaseOperationLogWriter> logger) : IOperationLogWriter
{
    private const int MaxSummaryLength = 8_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async ValueTask WriteAsync(
        OperationLogWriteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var httpContext = httpContextAccessor.HttpContext;
            var occurredAt = request.OccurredAt ?? timeProvider.GetUtcNow().UtcDateTime;
            var actor = Normalize(request.UserName, 50) ?? "system";
            var log = new OperationLog
            {
                UserId = request.UserId,
                UserName = Normalize(request.UserName, 50),
                ShowId = request.ShowId,
                OperationModule = NormalizeRequired(request.Module, 50, "UNKNOWN"),
                OperationType = NormalizeRequired(request.OperationType, 30, "UNKNOWN"),
                RequestUrl = Normalize(httpContext?.Request.Path.Value, 500),
                RequestParams = SerializeSummary(request.RequestSummary),
                ResponseResult = SerializeSummary(request.ResponseSummary),
                IpAddress = Normalize(
                    httpContext?.Connection.RemoteIpAddress?.ToString(),
                    50),
                UserAgent = Normalize(
                    httpContext?.Request.Headers.UserAgent.ToString(),
                    500),
                CostTime = request.CostTimeMilliseconds is null
                    ? null
                    : Math.Clamp(
                        request.CostTimeMilliseconds.Value,
                        0,
                        9_999_999_999L),
                Status = request.Succeeded,
                ErrorMsg = Normalize(request.ErrorMessage, 500),
                CreateTime = occurredAt,
                UpdateTime = occurredAt,
                CreateBy = actor,
                UpdateBy = actor,
            };

            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            dbContext.Set<OperationLog>().Add(log);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Operation log persistence failed for module {Module}, operation {OperationType}, error type {ErrorType}.",
                Normalize(request.Module, 50),
                Normalize(request.OperationType, 30),
                exception.GetType().Name);
        }
    }

    private static string? SerializeSummary(object? value)
    {
        if (value is null)
        {
            return null;
        }

        var json = JsonSerializer.Serialize(value, JsonOptions);
        return json.Length <= MaxSummaryLength
            ? json
            : JsonSerializer.Serialize(
                new { Truncated = true, OriginalLength = json.Length },
                JsonOptions);
    }

    private static string NormalizeRequired(
        string? value,
        int maxLength,
        string fallback) => Normalize(value, maxLength) ?? fallback;

    private static string? Normalize(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = string.Concat(value.Select(character =>
            char.IsControl(character) ? ' ' : character)).Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength];
    }
}
