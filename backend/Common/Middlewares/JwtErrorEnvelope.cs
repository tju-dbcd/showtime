using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using ShowtimeBackend.Common;

namespace ShowtimeBackend.Common.Middlewares;

/// <summary>
/// 将 JWT 鉴权失败（401）与权限不足（403）的响应体统一为 ApiResponse 信封，
/// 与业务错误（ApiResponse.Fail）保持一致，避免前端需要兼容多种错误格式。
/// </summary>
public static class JwtErrorEnvelope
{
    public static JwtBearerEvents Configure(JwtBearerEvents events)
    {
        events.OnChallenge = context =>
        {
            context.HandleResponse();

            var reason = context.ErrorDescription;
            if (string.IsNullOrWhiteSpace(reason) && context.Error is not null)
            {
                reason = context.Error;
            }

            var response = ApiResponse<object>.Fail(
                "AUTH_REQUIRED",
                string.IsNullOrWhiteSpace(reason)
                    ? "A valid JWT access token is required."
                    : reason);

            // HandleResponse() 后默认处理器不再写入状态码，需显式设置
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return WriteAsync(context.HttpContext, response);
        };

        events.OnForbidden = context =>
        {
            var response = ApiResponse<object>.Fail(
                "FORBIDDEN",
                "The current user lacks the required permission for this operation.");

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return WriteAsync(context.HttpContext, response);
        };

        return events;
    }

    private static Task WriteAsync(
        HttpContext httpContext,
        ApiResponse<object> response)
    {
        httpContext.Response.ContentType = "application/json; charset=utf-8";
        return httpContext.Response.WriteAsJsonAsync(
            response,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
}
