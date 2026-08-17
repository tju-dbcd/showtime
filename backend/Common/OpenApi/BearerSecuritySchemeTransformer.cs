using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace ShowtimeBackend.Common.OpenApi;

/// <summary>
/// 声明 Bearer 安全方案，并按 [Authorize] / [AllowAnonymous] 给操作注入 security 元数据，
/// 使 OpenAPI 文档（Scalar / openapi-typescript）能正确标注哪些接口需要 JWT。
/// </summary>
public sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Provide the JWT access token.",
        };

        // 基于路由端点元数据建立 {HTTP方法 路由} -> 是否需要鉴权 的映射
        var authByRoute = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var endpoints = context.ApplicationServices
            .GetRequiredService<EndpointDataSource>()
            .Endpoints;
        foreach (var endpoint in endpoints)
        {
            if (endpoint is not RouteEndpoint routeEndpoint)
            {
                continue;
            }

            var requiresAuth = endpoint.Metadata.GetMetadata<IAuthorizeData>() is not null
                               && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null;
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;
            if (methods is null || methods.Count == 0)
            {
                continue;
            }

            var normalized = NormalizeRoute(routeEndpoint.RoutePattern.RawText);
            foreach (var method in methods)
            {
                authByRoute[$"{method.ToUpperInvariant()} {normalized}"] = requiresAuth;
            }
        }

        var securityRequirement = new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", document)] = [],
        };

        foreach (var (path, pathItem) in document.Paths)
        {
            if (pathItem?.Operations is null)
            {
                continue;
            }

            foreach (var (method, operation) in pathItem.Operations)
            {
                var key = $"{method.ToString().ToUpperInvariant()} {NormalizeRoute(path.TrimStart('/'))}";
                if (authByRoute.TryGetValue(key, out var requiresAuth) && requiresAuth)
                {
                    operation.Security ??= new List<OpenApiSecurityRequirement>();
                    operation.Security.Add(securityRequirement);
                }
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 将路由模式归一化：去掉 {segment:constraint} 中的约束（如 {seatMapId:long} -> {seatMapId}），
    /// 以便与 OpenAPI 文档中的路径（不含约束）匹配。
    /// </summary>
    private static string NormalizeRoute(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        var segments = raw.Split('/');
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            if (segment.Length > 2 && segment[0] == '{' && segment[^1] == '}')
            {
                var colon = segment.IndexOf(':');
                if (colon > 0)
                {
                    segments[i] = "{" + segment[1..colon] + "}";
                }
            }
        }

        return string.Join('/', segments).TrimEnd('/');
    }
}
