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
        AssertSecurityApplied(paths, "/api/admin/orders", "get", expectApplied: true);
        AssertSecurityApplied(paths, "/api/admin/orders/{orderId}/cancel", "patch", expectApplied: true);
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


    [Fact]
    public async Task OpenApiDocument_DeclaresEnumConstraints_ForStatusFields()
    {
        using var factory = new AuthTestFactory();
        using var client = factory.CreateApiClient();

        var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        var schemas = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas");

        // 关键状态字段必须携带 enum 约束（枚举值进入 OpenAPI schema）
        AssertEnumValues(schemas, "UpdateSessionStatusRequest", "status",
            ["UPCOMING", "PRESALE", "ONSALE", "SOLD_OUT", "ENDED"]);
        AssertEnumValues(schemas, "CreatePriceStrategyRequest", "priceType",
            ["EARLY_BIRD", "PRESALE", "STANDARD", "VIP", "MEMBER"]);
        AssertEnumValues(schemas, "ShowDto", "status",
            ["DRAFT", "PUBLISHED", "UNPUBLISHED"]);
        AssertEnumValues(schemas, "ShowDto", "auditStatus",
            ["PENDING", "APPROVED", "REJECTED"]);
        AssertEnumValues(schemas, "ShowSessionDto", "sessionStatus",
            ["UPCOMING", "PRESALE", "ONSALE", "SOLD_OUT", "ENDED"]);
        AssertEnumValues(schemas, "OrderResponse", "orderStatus",
            ["PENDING_PAY", "PAID", "ISSUED", "PART_REFUND", "REFUNDED", "CANCELLED"]);
        AssertQueryParameterEnumValues(
            document.RootElement.GetProperty("paths"),
            schemas,
            "/api/admin/orders",
            "get",
            "Status",
            ["PENDING_PAY", "PAID", "ISSUED", "PART_REFUND", "REFUNDED", "CANCELLED"]);
    }

    private static void AssertQueryParameterEnumValues(
        JsonElement paths,
        JsonElement schemas,
        string path,
        string method,
        string parameterName,
        string[] expectedValues)
    {
        var parameter = paths.GetProperty(path)
            .GetProperty(method)
            .GetProperty("parameters")
            .EnumerateArray()
            .Single(item => item.GetProperty("name").GetString() == parameterName);
        var schema = parameter.GetProperty("schema");
        var componentName = schema.GetProperty("$ref").GetString()!.Split('/').Last();
        var enumElement = schemas.GetProperty(componentName).GetProperty("enum");
        var actual = enumElement.EnumerateArray()
            .Select(item => item.GetString())
            .OrderBy(value => value)
            .ToArray();

        Assert.Equal(expectedValues.OrderBy(value => value), actual);
    }

    private static void AssertEnumValues(
        JsonElement schemas,
        string schemaName,
        string propertyName,
        string[] expectedValues)
    {
        Assert.True(
            schemas.TryGetProperty(schemaName, out var schema),
            $"Schema '{schemaName}' should exist in OpenAPI document.");
        Assert.True(
            schema.GetProperty("properties").TryGetProperty(propertyName, out var property),
            $"Property '{schemaName}.{propertyName}' should exist.");

        // 枚举属性以 $ref 指向组件；解引用后断言 enum 约束
        if (property.TryGetProperty("$ref", out var reference))
        {
            var componentName = reference.GetString()!.Split('/').Last();
            Assert.True(
                schemas.TryGetProperty(componentName, out var component),
                $"Referenced schema '{componentName}' should exist.");
            property = component;
        }

        Assert.True(
            property.TryGetProperty("enum", out var enumElement),
            $"Property '{schemaName}.{propertyName}' should declare enum values.");
        var actual = enumElement.EnumerateArray()
            .Select(item => item.GetString())
            .OrderBy(value => value)
            .ToArray();
        Assert.Equal(
            expectedValues.OrderBy(value => value),
            actual);
        Assert.Equal("string", property.GetProperty("type").GetString());
    }
}
