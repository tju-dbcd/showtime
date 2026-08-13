using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using ShowtimeBackend.Common;

namespace ShowtimeBackend.Common.Middlewares;

/// <summary>
/// 未处理异常的兜底处理器：将 500 响应体统一为 ApiResponse 信封，
/// 与业务错误（ApiResponse.Fail）保持一致。错误细节通过日志输出，不回传给客户端。
/// </summary>
public sealed class ApiResponseExceptionHandler : IExceptionHandler
{
    private readonly ILogger<ApiResponseExceptionHandler> _logger;

    public ApiResponseExceptionHandler(ILogger<ApiResponseExceptionHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        _logger.LogError(
            exception,
            "Unhandled exception while processing {Method} {Path}.",
            httpContext.Request.Method,
            httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        httpContext.Response.ContentType = "application/json; charset=utf-8";

        var response = ApiResponse<object>.Fail(
            "INTERNAL_ERROR",
            "An unexpected error occurred. Please try again later.");

        return new ValueTask<bool>(
            httpContext.Response.WriteAsJsonAsync(
                response,
                new JsonSerializerOptions(JsonSerializerDefaults.Web),
                cancellationToken)
            .ContinueWith(_ => true, cancellationToken));
    }
}
