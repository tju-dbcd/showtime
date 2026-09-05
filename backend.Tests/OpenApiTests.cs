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
        AssertSecurityApplied(paths, "/api/orders/{orderId}/tickets", "get", expectApplied: true);
        AssertSecurityApplied(paths, "/api/admin/orders/{orderId}/issue", "post", expectApplied: true);
        AssertSecurityApplied(paths, "/api/admin/seat-maps", "get", expectApplied: true);
        AssertSecurityApplied(paths, "/api/admin/seat-rules", "post", expectApplied: true);

        // 匿名：/api/auth/login、/api/sessions/{sessionId}/seat-map、/api/client/...
        AssertSecurityApplied(paths, "/api/auth/login", "post", expectApplied: false);
        AssertSecurityApplied(paths, "/api/sessions/{sessionId}/seat-map", "get", expectApplied: false);
        AssertSecurityApplied(paths, "/api/client/shows/{showId}/sessions", "get", expectApplied: false);
    }

    [Fact]
    public async Task OpenApiDocument_DeclaresRequiredOrderIdempotencyHeader()
    {
        using var factory = new AuthTestFactory();
        using var client = factory.CreateApiClient();

        var response = await client.GetAsync("/openapi/v1.json");

        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        var parameters = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/orders")
            .GetProperty("post")
            .GetProperty("parameters");
        var parameter = Assert.Single(parameters.EnumerateArray());
        Assert.Equal("Idempotency-Key", parameter.GetProperty("name").GetString());
        Assert.Equal("header", parameter.GetProperty("in").GetString());
        Assert.True(parameter.GetProperty("required").GetBoolean());
        var schema = parameter.GetProperty("schema");
        Assert.Equal("string", schema.GetProperty("type").GetString());
        Assert.Equal(64, schema.GetProperty("maxLength").GetInt32());
    }

    [Fact]
    public async Task OpenApiDocument_DeclaresSeatBatchUpdateContract()
    {
        using var factory = new AuthTestFactory();
        using var client = factory.CreateApiClient();

        var response = await client.GetAsync("/openapi/v1.json");

        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        var operation = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/admin/seat-sections/{seatSectionId}/seats")
            .GetProperty("patch");

        var requestSchema = operation
            .GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema");
        Assert.Equal(
            "#/components/schemas/SeatBatchUpdateRequest",
            requestSchema.GetProperty("$ref").GetString());

        var responses = operation.GetProperty("responses");
        Assert.True(responses.TryGetProperty("200", out _));
        Assert.True(responses.TryGetProperty("400", out _));
        Assert.True(responses.TryGetProperty("404", out _));

        AssertSecurityApplied(
            document.RootElement.GetProperty("paths"),
            "/api/admin/seat-sections/{seatSectionId}/seats",
            "patch",
            expectApplied: true);
    }

    [Fact]
    public async Task OpenApiDocument_DeclaresTicketIssuanceContracts()
    {
        using var factory = new AuthTestFactory();
        using var client = factory.CreateApiClient();

        var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var paths = root.GetProperty("paths");
        var schemas = root.GetProperty("components").GetProperty("schemas");

        Assert.True(paths.TryGetProperty("/api/orders/{orderId}/tickets", out var tickets));
        Assert.True(tickets.TryGetProperty("get", out _));
        Assert.True(paths.TryGetProperty("/api/admin/orders/{orderId}/issue", out var issue));
        Assert.True(issue.TryGetProperty("post", out var issuePost));
        Assert.True(paths.TryGetProperty("/api/orders/{orderId}/payments/mock", out var payment));
        Assert.True(payment.TryGetProperty("post", out var paymentPost));

        AssertResponseCodes(issuePost, "200", "401", "403", "404", "409", "500");
        AssertResponseCodes(paymentPost, "200", "400", "401", "404", "409", "500");

        AssertSchemaProperties(
            schemas,
            "TicketResponse",
            "eTicketId",
            "eTicketNo",
            "orderItemId",
            "ticketStatus",
            "qrCode");
        AssertSchemaProperties(
            schemas,
            "PaymentProcessResponse",
            "payment",
            "orderStatus",
            "issuedTicketCount");
        AssertSchemaProperties(
            schemas,
            "TicketIssuanceResponse",
            "orderId",
            "orderStatus",
            "createdTicketCount",
            "existingTicketCount",
            "totalTicketCount",
            "issueTime");
        AssertSchemaProperties(schemas, "OrderResponse", "issueTime");
    }

    [Fact]
    public async Task OpenApiDocument_DeclaresTicketRedemptionContract()
    {
        using var factory = new AuthTestFactory();
        using var client = factory.CreateApiClient();

        using var document = JsonDocument.Parse(
            await client.GetStringAsync("/openapi/v1.json"));
        var root = document.RootElement;
        var paths = root.GetProperty("paths");
        var operation = paths
            .GetProperty("/api/admin/tickets/redeem")
            .GetProperty("post");
        var schemas = root.GetProperty("components").GetProperty("schemas");
        var requestSchema = schemas.GetProperty("RedeemTicketRequest");
        var required = requestSchema.GetProperty("required")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToHashSet();

        Assert.Contains("qrCode", required);
        Assert.Contains("checkDevice", required);
        var qrCode = requestSchema.GetProperty("properties").GetProperty("qrCode");
        var checkDevice = requestSchema.GetProperty("properties").GetProperty("checkDevice");
        Assert.Equal("string", qrCode.GetProperty("type").GetString());
        Assert.Equal(255, qrCode.GetProperty("maxLength").GetInt32());
        Assert.Equal("string", checkDevice.GetProperty("type").GetString());
        Assert.Equal(100, checkDevice.GetProperty("maxLength").GetInt32());
        AssertResponseCodes(operation, "200", "400", "401", "403", "404", "409", "500");
        AssertSecurityApplied(
            paths,
            "/api/admin/tickets/redeem",
            "post",
            expectApplied: true);
        AssertSchemaProperties(
            schemas,
            "TicketRedemptionResponse",
            "eTicketId",
            "eTicketNo",
            "orderId",
            "orderItemId",
            "sessionId",
            "ticketStatus",
            "checkTime",
            "checkDevice",
            "checkBy");
    }

    [Fact]
    public async Task OpenApiDocument_DeclaresRefundWorkflowContracts()
    {
        using var factory = new AuthTestFactory();
        using var client = factory.CreateApiClient();

        var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var paths = root.GetProperty("paths");
        var schemas = root.GetProperty("components").GetProperty("schemas");

        AssertOperationExists(paths, "/api/orders/{orderId}/refunds/quote", "post");
        AssertOperationExists(paths, "/api/orders/{orderId}/refunds", "post");
        AssertOperationExists(paths, "/api/orders/{orderId}/refunds", "get");
        AssertOperationExists(paths, "/api/refunds/{refundId}", "get");
        AssertOperationExists(paths, "/api/admin/refunds", "get");
        AssertOperationExists(paths, "/api/admin/refunds/{refundId}", "get");
        AssertOperationExists(paths, "/api/admin/refunds/{refundId}/approve", "post");
        AssertOperationExists(paths, "/api/admin/refunds/{refundId}/reject", "post");
        AssertOperationExists(paths, "/api/admin/refund-policies", "get");
        AssertOperationExists(paths, "/api/admin/refund-policies", "post");
        AssertOperationExists(paths, "/api/admin/refund-policies/{policyId}", "put");
        AssertOperationExists(paths, "/api/admin/refund-policies/{policyId}/status", "patch");

        AssertSchemaProperties(
            schemas,
            "RefundResponse",
            "refundId",
            "refundNo",
            "orderId",
            "userId",
            "refundType",
            "refundReason",
            "appliedPolicyId",
            "policyName",
            "refundAmount",
            "feeRate",
            "appliedServiceFee",
            "actualRefund",
            "approveStatus",
            "refundStatus",
            "reviewBy",
            "reviewTime",
            "reviewRemark",
            "completeTime",
            "createTime",
            "items");
        AssertEnumValues(schemas, "RefundResponse", "refundType", ["FULL", "PART"]);
        AssertEnumValues(
            schemas,
            "RefundResponse",
            "approveStatus",
            ["PENDING", "APPROVED", "REJECTED"]);
        AssertEnumValues(
            schemas,
            "RefundResponse",
            "refundStatus",
            ["PENDING", "PROCESSING", "COMPLETED", "FAILED"]);
        AssertEnumValues(
            schemas,
            "RefundItemResponse",
            "itemStatus",
            ["NORMAL", "REFUNDING", "REFUNDED", "EXCHANGING", "EXCHANGED"]);
        AssertEnumValues(
            schemas,
            "RefundItemResponse",
            "ticketStatus",
            ["UNUSED", "REFUNDING", "USED", "REFUNDED", "EXCHANGING", "EXCHANGED"]);
        AssertQueryParameterEnumValues(
            paths,
            schemas,
            "/api/orders/{orderId}/refunds",
            "get",
            "ApproveStatus",
            ["PENDING", "APPROVED", "REJECTED"]);
        AssertQueryParameterEnumValues(
            paths,
            schemas,
            "/api/orders/{orderId}/refunds",
            "get",
            "RefundStatus",
            ["PENDING", "PROCESSING", "COMPLETED", "FAILED"]);
        AssertQueryParameterEnumValues(
            paths,
            schemas,
            "/api/admin/refunds",
            "get",
            "ApproveStatus",
            ["PENDING", "APPROVED", "REJECTED"]);
        AssertQueryParameterEnumValues(
            paths,
            schemas,
            "/api/admin/refunds",
            "get",
            "RefundStatus",
            ["PENDING", "PROCESSING", "COMPLETED", "FAILED"]);
    }

    [Fact]
    public async Task OpenApiDocument_MarksEveryRefundOperationWithBearerSecurity()
    {
        using var factory = new AuthTestFactory();
        using var client = factory.CreateApiClient();

        using var document = JsonDocument.Parse(
            await client.GetStringAsync("/openapi/v1.json"));
        var paths = document.RootElement.GetProperty("paths");

        AssertSecurityApplied(paths, "/api/orders/{orderId}/refunds/quote", "post", expectApplied: true);
        AssertSecurityApplied(paths, "/api/orders/{orderId}/refunds", "post", expectApplied: true);
        AssertSecurityApplied(paths, "/api/orders/{orderId}/refunds", "get", expectApplied: true);
        AssertSecurityApplied(paths, "/api/refunds/{refundId}", "get", expectApplied: true);
        AssertSecurityApplied(paths, "/api/admin/refunds", "get", expectApplied: true);
        AssertSecurityApplied(paths, "/api/admin/refunds/{refundId}", "get", expectApplied: true);
        AssertSecurityApplied(paths, "/api/admin/refunds/{refundId}/approve", "post", expectApplied: true);
        AssertSecurityApplied(paths, "/api/admin/refunds/{refundId}/reject", "post", expectApplied: true);
        AssertSecurityApplied(paths, "/api/admin/refund-policies", "get", expectApplied: true);
        AssertSecurityApplied(paths, "/api/admin/refund-policies", "post", expectApplied: true);
        AssertSecurityApplied(paths, "/api/admin/refund-policies/{policyId}", "put", expectApplied: true);
        AssertSecurityApplied(paths, "/api/admin/refund-policies/{policyId}/status", "patch", expectApplied: true);
    }

    [Fact]
    public async Task OpenApiDocument_DeclaresExactRefundApiResponseContracts()
    {
        using var factory = new AuthTestFactory();
        using var client = factory.CreateApiClient();

        using var document = JsonDocument.Parse(
            await client.GetStringAsync("/openapi/v1.json"));
        var root = document.RootElement;
        var paths = root.GetProperty("paths");
        var schemas = root.GetProperty("components").GetProperty("schemas");

        AssertApiResponseContract(
            paths,
            schemas,
            "/api/orders/{orderId}/refunds/quote",
            "post",
            "RefundQuoteResponse",
            "200", "400", "401", "404", "409");
        AssertApiResponseContract(
            paths,
            schemas,
            "/api/orders/{orderId}/refunds",
            "post",
            "RefundResponse",
            "201", "400", "401", "404", "409", "500");
        AssertApiResponseContract(
            paths,
            schemas,
            "/api/orders/{orderId}/refunds",
            "get",
            "PagedRefundResponse",
            "200", "400", "401", "404");
        AssertApiResponseContract(
            paths,
            schemas,
            "/api/refunds/{refundId}",
            "get",
            "RefundResponse",
            "200", "401", "404");
        AssertApiResponseContract(
            paths,
            schemas,
            "/api/admin/refunds",
            "get",
            "PagedRefundResponse",
            "200", "400", "401", "403");
        AssertApiResponseContract(
            paths,
            schemas,
            "/api/admin/refunds/{refundId}",
            "get",
            "RefundResponse",
            "200", "401", "403", "404");
        AssertApiResponseContract(
            paths,
            schemas,
            "/api/admin/refunds/{refundId}/approve",
            "post",
            "RefundResponse",
            "200", "400", "401", "403", "404", "409", "500");
        AssertApiResponseContract(
            paths,
            schemas,
            "/api/admin/refunds/{refundId}/reject",
            "post",
            "RefundResponse",
            "200", "400", "401", "403", "404", "409", "500");
        AssertApiResponseContract(
            paths,
            schemas,
            "/api/admin/refund-policies",
            "get",
            "PagedRefundPolicyResponse",
            "200", "400", "401", "403");
        AssertApiResponseContract(
            paths,
            schemas,
            "/api/admin/refund-policies",
            "post",
            "RefundPolicyResponse",
            "201", "400", "401", "403", "404");
        AssertApiResponseContract(
            paths,
            schemas,
            "/api/admin/refund-policies/{policyId}",
            "put",
            "RefundPolicyResponse",
            "200", "400", "401", "403", "404");
        AssertApiResponseContract(
            paths,
            schemas,
            "/api/admin/refund-policies/{policyId}/status",
            "patch",
            "RefundPolicyResponse",
            "200", "400", "401", "403", "404");
    }

    [Fact]
    public async Task OpenApiDocument_DeclaresExchangeWorkflowContracts()
    {
        using var factory = new AuthTestFactory();
        using var client = factory.CreateApiClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var root = document.RootElement;
        var paths = root.GetProperty("paths");
        var schemas = root.GetProperty("components").GetProperty("schemas");

        (string Path, string Method)[] operations =
        [
            ("/api/orders/{orderId}/exchanges/quote", "post"),
            ("/api/orders/{orderId}/exchanges", "post"),
            ("/api/orders/{orderId}/exchanges", "get"),
            ("/api/exchanges/{exchangeId}", "get"),
            ("/api/exchanges/{exchangeId}/pay", "post"),
            ("/api/admin/exchanges", "get"),
            ("/api/admin/exchanges/{exchangeId}", "get"),
            ("/api/admin/exchanges/{exchangeId}/reject", "post"),
            ("/api/admin/exchanges/{exchangeId}/approve", "post"),
            ("/api/admin/exchange-policies", "get"),
            ("/api/admin/exchange-policies", "post"),
            ("/api/admin/exchange-policies/{policyId}", "put"),
            ("/api/admin/exchange-policies/{policyId}/status", "patch"),
        ];
        foreach (var operation in operations)
        {
            AssertOperationExists(paths, operation.Path, operation.Method);
            AssertSecurityApplied(paths, operation.Path, operation.Method, expectApplied: true);
        }

        AssertSchemaProperties(schemas, "ExchangeQuoteResponse",
            "quotedAt", "orderId", "origSessionId", "targetSessionId", "origDeduction",
            "targetAmount", "priceDiff", "exchangeFee", "amountDue", "appliedPolicyId",
            "policyName", "items");
        AssertSchemaProperties(schemas, "ExchangeResponse",
            "exchangeId", "exchangeNo", "originalOrderId", "childOrderId", "userId",
            "origSessionId", "targetSessionId", "origDeduction", "targetAmount", "priceDiff",
            "exchangeFee", "amountDue", "approveStatus", "exchangeStatus", "expireTime", "items");
        AssertEnumValues(schemas, "ExchangeResponse", "approveStatus",
            ["PENDING", "APPROVED", "REJECTED"]);
        AssertEnumValues(schemas, "ExchangeResponse", "exchangeStatus",
            ["PENDING", "PROCESSING", "COMPLETED", "FAILED"]);
        AssertSchemaProperties(schemas, "OrderResponse",
            "orderType", "parentOrderId", "canPay", "canCancel");

        AssertApiResponseContract(paths, schemas,
            "/api/orders/{orderId}/exchanges/quote", "post",
            "ExchangeQuoteResponse", "200", "400", "401", "404", "409");
        AssertApiResponseContract(paths, schemas,
            "/api/orders/{orderId}/exchanges", "post",
            "ExchangeResponse", "201", "400", "401", "404", "409");
        AssertApiResponseContract(paths, schemas,
            "/api/orders/{orderId}/exchanges", "get",
            "PagedExchangeResponse", "200", "400", "401", "404");
        AssertApiResponseContract(paths, schemas,
            "/api/exchanges/{exchangeId}", "get",
            "ExchangeResponse", "200", "401", "404", "409");
        AssertApiResponseContract(paths, schemas,
            "/api/exchanges/{exchangeId}/pay", "post",
            "ExchangePaymentResponse", "200", "400", "401", "404", "409");
        AssertApiResponseContract(paths, schemas,
            "/api/admin/exchanges", "get",
            "PagedExchangeResponse", "200", "400", "401", "403");
        AssertApiResponseContract(paths, schemas,
            "/api/admin/exchanges/{exchangeId}", "get",
            "ExchangeResponse", "200", "401", "403", "404", "409");
        AssertApiResponseContract(paths, schemas,
            "/api/admin/exchanges/{exchangeId}/reject", "post",
            "ExchangeResponse", "200", "400", "401", "403", "404", "409");
        AssertApiResponseContract(paths, schemas,
            "/api/admin/exchanges/{exchangeId}/approve", "post",
            "ExchangeResponse", "200", "400", "401", "403", "404", "409");
        AssertApiResponseContract(paths, schemas,
            "/api/admin/exchange-policies", "get",
            "PagedExchangePolicyResponse", "200", "400", "401", "403");
        AssertApiResponseContract(paths, schemas,
            "/api/admin/exchange-policies", "post",
            "ExchangePolicyResponse", "201", "400", "401", "403", "404");
        AssertApiResponseContract(paths, schemas,
            "/api/admin/exchange-policies/{policyId}", "put",
            "ExchangePolicyResponse", "200", "400", "401", "403", "404");
        AssertApiResponseContract(paths, schemas,
            "/api/admin/exchange-policies/{policyId}/status", "patch",
            "ExchangePolicyResponse", "200", "400", "401", "403", "404");
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

    private static void AssertSchemaProperties(
        JsonElement schemas,
        string schemaName,
        params string[] propertyNames)
    {
        var properties = schemas.GetProperty(schemaName).GetProperty("properties");
        foreach (var propertyName in propertyNames)
        {
            Assert.True(
                properties.TryGetProperty(propertyName, out _),
                $"Property '{schemaName}.{propertyName}' should exist.");
        }
    }

    private static void AssertOperationExists(
        JsonElement paths,
        string path,
        string method)
    {
        Assert.True(paths.TryGetProperty(path, out var pathItem), $"Path '{path}' should exist.");
        Assert.True(
            pathItem.TryGetProperty(method, out _),
            $"Operation '{method.ToUpperInvariant()} {path}' should exist.");
    }

    private static void AssertApiResponseContract(
        JsonElement paths,
        JsonElement schemas,
        string path,
        string method,
        string dataSchemaName,
        params string[] statusCodes)
    {
        var responses = paths.GetProperty(path)
            .GetProperty(method)
            .GetProperty("responses");
        var actualStatusCodes = responses.EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(statusCode => statusCode)
            .ToArray();
        Assert.Equal(statusCodes.OrderBy(statusCode => statusCode), actualStatusCodes);

        string? apiResponseReference = null;
        foreach (var statusCode in statusCodes)
        {
            var currentReference = responses.GetProperty(statusCode)
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema")
                .GetProperty("$ref")
                .GetString();
            Assert.False(
                string.IsNullOrWhiteSpace(currentReference),
                $"Response '{statusCode}' for {method.ToUpperInvariant()} {path} should use an ApiResponse schema.");
            apiResponseReference ??= currentReference;
            Assert.Equal(apiResponseReference, currentReference);
        }

        var apiResponseSchemaName = apiResponseReference!.Split('/').Last();
        var apiResponseProperties = schemas.GetProperty(apiResponseSchemaName).GetProperty("properties");
        foreach (var propertyName in new[] { "success", "data", "code", "message" })
        {
            Assert.True(
                apiResponseProperties.TryGetProperty(propertyName, out _),
                $"Property '{apiResponseSchemaName}.{propertyName}' should exist.");
        }

        var dataReference = apiResponseProperties.GetProperty("data")
            .GetProperty("oneOf")
            .EnumerateArray()
            .Single(item => item.TryGetProperty("$ref", out _))
            .GetProperty("$ref")
            .GetString();
        Assert.Equal($"#/components/schemas/{dataSchemaName}", dataReference);
    }

    private static void AssertResponseCodes(
        JsonElement operation,
        params string[] statusCodes)
    {
        var responses = operation.GetProperty("responses");
        foreach (var statusCode in statusCodes)
        {
            Assert.True(
                responses.TryGetProperty(statusCode, out _),
                $"Response status '{statusCode}' should be declared.");
        }
    }
}
