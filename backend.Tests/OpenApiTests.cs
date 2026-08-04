using System.Text.Json;

namespace ShowtimeBackend.Tests;

public sealed class OpenApiTests
{
    [Fact]
    public async Task OpenApiDocument_DeclaresJwtBearerScheme()
    {
        using var factory = new AuthTestFactory();
        using var client = factory.CreateApiClient();

        var response = await client.GetAsync("/openapi/v1.json");

        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        var bearer = document.RootElement
            .GetProperty("components")
            .GetProperty("securitySchemes")
            .GetProperty("Bearer");
        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());
        Assert.Equal("JWT", bearer.GetProperty("bearerFormat").GetString());
    }

    [Fact]
    public async Task OpenApiDocument_MarksAuthorizedOperationsWithBearerSecurity()
    {
        using var factory = new AuthTestFactory();
        using var client = factory.CreateApiClient();

        var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");

        // 需鉴权：/api/orders GET、/api/admin/seat-maps GET（新增管理端鉴权后）
        AssertSecurityApplied(paths, "/api/orders", "get", expectApplied: true);
        AssertSecurityApplied(paths, "/api/admin/seat-maps", "get", expectApplied: true);
        AssertSecurityApplied(paths, "/api/admin/seat-rules", "post", expectApplied: true);

        // 匿名：/api/auth/login、/api/sessions/{sessionId}/seat-map、/api/client/...
        AssertSecurityApplied(paths, "/api/auth/login", "post", expectApplied: false);
        AssertSecurityApplied(paths, "/api/sessions/{sessionId}/seat-map", "get", expectApplied: false);
        AssertSecurityApplied(paths, "/api/client/shows/{showId}/sessions", "get", expectApplied: false);
    }

    private static void AssertSecurityApplied(
        JsonElement paths,
        string path,
        string method,
        bool expectApplied)
    {
        var operation = paths.GetProperty(path).GetProperty(method);
        if (expectApplied)
        {
            var security = operation.GetProperty("security");
            Assert.True(
                security.GetArrayLength() > 0,
                $"{method} {path} 应带有 Bearer security");
        }
        else if (operation.TryGetProperty("security", out var security))
        {
            Assert.True(
                security.GetArrayLength() == 0,
                $"{method} {path} 不应带有 Bearer security");
        }
    }
}
