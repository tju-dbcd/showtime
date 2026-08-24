using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Entities.SeatZone;
using ShowtimeBackend.Services.OrderTicket;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class RefundControllersTests
{
    [Fact]
    public async Task OpenApi_DeclaresRefundOperationErrorResponseEnvelopes()
    {
        using var factory = new AuthTestFactory();
        using var client = factory.CreateApiClient();

        var response = await client.GetAsync("/openapi/v1.json");

        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        AssertOperationResponses(
            paths,
            "/api/orders/{orderId}/refunds/quote",
            "post",
            "200",
            "400",
            "401",
            "404",
            "409");
        AssertOperationResponses(
            paths,
            "/api/orders/{orderId}/refunds",
            "post",
            "201",
            "400",
            "401",
            "404",
            "409",
            "500");
        AssertOperationResponses(
            paths,
            "/api/orders/{orderId}/refunds",
            "get",
            "200",
            "400",
            "401",
            "404");
        AssertOperationResponses(
            paths,
            "/api/refunds/{refundId}",
            "get",
            "200",
            "401",
            "404");
        AssertOperationResponses(
            paths,
            "/api/admin/refunds",
            "get",
            "200",
            "400",
            "401",
            "403");
        AssertOperationResponses(
            paths,
            "/api/admin/refunds/{refundId}",
            "get",
            "200",
            "401",
            "403",
            "404");
        AssertOperationResponses(
            paths,
            "/api/admin/refunds/{refundId}/approve",
            "post",
            "200",
            "400",
            "401",
            "403",
            "404",
            "409",
            "500");
        AssertOperationResponses(
            paths,
            "/api/admin/refunds/{refundId}/reject",
            "post",
            "200",
            "400",
            "401",
            "403",
            "404",
            "409",
            "500");
    }

    [Fact]
    public async Task GetAsync_WhenAppliedPolicyIsMissing_ReturnsResponseWithNullPolicyName()
    {
        await using var fixture = await RefundTestData.CreateLegacyRefundAsync(
            appliedPolicyId: null);

        var result = await fixture.CreateApplicationService().GetAsync(
            fixture.UserId,
            fixture.RefundId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.PolicyName);
        Assert.Equal(fixture.OrderItemIds, result.Value.Items.Select(x => x.OrderItemId));
    }

    [Fact]
    public async Task GetAsync_WhenPolicyRelationIsBroken_ReturnsResponseWithNullPolicyName()
    {
        await using var fixture = await RefundTestData.CreateLegacyRefundAsync(
            appliedPolicyId: 999);

        var result = await fixture.CreateApplicationService().GetAsync(
            fixture.UserId,
            fixture.RefundId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.PolicyName);
    }

    [Fact]
    public async Task GetAsync_WhenOwnedByAnotherUser_ReturnsNotFound()
    {
        await using var fixture = await RefundTestData.CreateLegacyRefundAsync(
            appliedPolicyId: null);

        var result = await fixture.CreateApplicationService().GetAsync(
            fixture.UserId + 1,
            fixture.RefundId,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderTicketFailure.NotFound, result.Failure);
        Assert.Equal("REFUND_NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public async Task ListAsync_ReturnsOnlyOwnedOrderRefundsInStableDescendingOrder()
    {
        await using var fixture = await RefundTestData.CreateLegacyRefundAsync(
            appliedPolicyId: null);
        fixture.Db.AddRange(
            Refund(402, fixture.OrderId, fixture.UserId, RefundTestData.FixedUtcNow),
            Refund(403, fixture.OrderId, fixture.UserId, RefundTestData.FixedUtcNow),
            Refund(404, fixture.OrderId, fixture.UserId + 1, RefundTestData.FixedUtcNow.AddHours(1)),
            Refund(405, fixture.OrderId + 1, fixture.UserId, RefundTestData.FixedUtcNow.AddHours(2)));
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var firstPage = await fixture.CreateApplicationService().ListAsync(
            fixture.UserId,
            fixture.OrderId,
            new RefundListQuery(null, null, 1, 2),
            CancellationToken.None);
        var secondPage = await fixture.CreateApplicationService().ListAsync(
            fixture.UserId,
            fixture.OrderId,
            new RefundListQuery(null, null, 2, 2),
            CancellationToken.None);

        Assert.True(firstPage.IsSuccess);
        Assert.Equal(3, firstPage.Value!.TotalCount);
        Assert.Equal([403L, 402L], firstPage.Value.Items.Select(x => x.RefundId));
        Assert.True(secondPage.IsSuccess);
        Assert.Equal(3, secondPage.Value!.TotalCount);
        Assert.Equal([fixture.RefundId], secondPage.Value.Items.Select(x => x.RefundId));
    }

    [Fact]
    public async Task ListAsync_WhenOrderBelongsToAnotherUser_ReturnsNotFound()
    {
        await using var fixture = await RefundTestData.CreateLegacyRefundAsync(
            appliedPolicyId: null);

        var result = await fixture.CreateApplicationService().ListAsync(
            fixture.UserId + 1,
            fixture.OrderId,
            new RefundListQuery(null, null),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderTicketFailure.NotFound, result.Failure);
        Assert.Equal("REFUND_ORDER_NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public async Task ListAsync_WithStatusFilters_CountsOnlyMatchingRefunds()
    {
        await using var fixture = await RefundTestData.CreateLegacyRefundAsync(
            appliedPolicyId: null);
        var completed = Refund(
            402,
            fixture.OrderId,
            fixture.UserId,
            RefundTestData.FixedUtcNow);
        completed.ApproveStatus = "APPROVED";
        completed.RefundStatus = "COMPLETED";
        completed.CompleteTime = RefundTestData.FixedUtcNow;
        fixture.Db.Add(completed);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var result = await fixture.CreateApplicationService().ListAsync(
            fixture.UserId,
            fixture.OrderId,
            new RefundListQuery(
                RefundApproveStatus.APPROVED,
                RefundStatus.COMPLETED),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.TotalCount);
        var item = Assert.Single(result.Value.Items);
        Assert.Equal(402, item.RefundId);
        Assert.Equal(RefundStatus.COMPLETED, item.RefundStatus);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    [InlineData(int.MaxValue, 100)]
    public async Task ListAsync_WhenPagingIsInvalid_ReturnsInvalidRequest(int page, int pageSize)
    {
        await using var fixture = await RefundTestData.CreateLegacyRefundAsync(
            appliedPolicyId: null);

        var result = await fixture.CreateApplicationService().ListAsync(
            fixture.UserId,
            fixture.OrderId,
            new RefundListQuery(null, null, page, pageSize),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderTicketFailure.InvalidRequest, result.Failure);
        Assert.Equal("REFUND_INVALID_PAGING", result.ErrorCode);
    }

    [Fact]
    public async Task Quote_WithoutAuthentication_ReturnsUnauthorized()
    {
        using var factory = new AuthTestFactory();
        using var client = factory.CreateApiClient();

        var response = await client.PostAsJsonAsync(
            "/api/orders/10/refunds/quote",
            new RefundQuoteRequest([1L]));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Quote_WithEligibleItem_ReturnsOkEnvelope()
    {
        using var factory = new AuthTestFactory();
        await RefundTestData.SeedIssuedOrderAsync(factory);
        using var client = factory.CreateApiClient();
        Authenticate(client, 7);

        var response = await client.PostAsJsonAsync(
            "/api/orders/10/refunds/quote",
            new RefundQuoteRequest([1L]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadEnumResponseAsync<RefundQuoteResponse>(response);
        Assert.True(body.Success);
        Assert.Equal(84m, body.Data!.ActualRefund);
    }

    [Fact]
    public async Task Create_WithValidUser_ReturnsCreatedAndLocation()
    {
        using var factory = new AuthTestFactory();
        await RefundTestData.SeedIssuedOrderAsync(factory);
        using var refundFactory = CreateRefundFactory(factory);
        using var client = CreateApiClient(refundFactory);
        Authenticate(client, 7);

        var response = await client.PostAsJsonAsync(
            "/api/orders/10/refunds",
            new CreateRefundRequest([1L], "行程变更"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/api/refunds/1", response.Headers.Location!.ToString());
        var body = await ReadEnumResponseAsync<RefundResponse>(response);
        Assert.True(body.Success);
        Assert.Equal(1, body.Data!.RefundId);
    }

    [Fact]
    public async Task Create_WithIneligibleItem_ReturnsConflictEnvelope()
    {
        using var factory = new AuthTestFactory();
        await RefundTestData.SeedIssuedOrderAsync(factory);
        using var refundFactory = CreateRefundFactory(factory);
        using var client = CreateApiClient(refundFactory);
        Authenticate(client, 7);

        var response = await client.PostAsJsonAsync(
            "/api/orders/10/refunds",
            new CreateRefundRequest([999L], "行程变更"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await ReadEnumResponseAsync<RefundResponse>(response);
        Assert.False(body.Success);
        Assert.Equal("REFUND_ITEM_NOT_ELIGIBLE", body.Code);
    }

    [Fact]
    public async Task List_WithInvalidPaging_ReturnsBadRequestEnvelope()
    {
        using var factory = new AuthTestFactory();
        await RefundTestData.SeedIssuedOrderAsync(factory);
        using var client = factory.CreateApiClient();
        Authenticate(client, 7);

        var response = await client.GetAsync(
            "/api/orders/10/refunds?page=0&pageSize=20");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ReadEnumResponseAsync<PagedRefundResponse>(response);
        Assert.False(body.Success);
        Assert.Equal("REFUND_INVALID_PAGING", body.Code);
    }

    [Fact]
    public async Task List_WithOwnedOrder_ReturnsOkEnvelope()
    {
        using var factory = new AuthTestFactory();
        await RefundTestData.SeedIssuedOrderAsync(factory);
        using var client = factory.CreateApiClient();
        Authenticate(client, 7);

        var response = await client.GetAsync("/api/orders/10/refunds");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadEnumResponseAsync<PagedRefundResponse>(response);
        Assert.True(body.Success);
        Assert.Empty(body.Data!.Items);
        Assert.Equal(0, body.Data.TotalCount);
    }

    [Fact]
    public async Task Get_WhenOwnedByAnotherUser_ReturnsNotFound()
    {
        using var factory = new AuthTestFactory();
        await SeedLegacyRefundAsync(factory, appliedPolicyId: null);
        using var client = factory.CreateApiClient();
        Authenticate(client, 8);

        var response = await client.GetAsync("/api/refunds/401");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await ReadEnumResponseAsync<RefundResponse>(response);
        Assert.Equal("REFUND_NOT_FOUND", body.Code);
    }

    [Fact]
    public async Task Get_WhenAppliedPolicyIsMissing_SerializesNullPolicyName()
    {
        using var factory = new AuthTestFactory();
        await SeedLegacyRefundAsync(factory, appliedPolicyId: null);
        using var client = factory.CreateApiClient();
        Authenticate(client, 7);

        var response = await client.GetAsync("/api/refunds/401");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadEnumResponseAsync<RefundResponse>(response);
        Assert.True(body.Success);
        Assert.Null(body.Data!.PolicyName);
    }

    [Fact]
    public async Task AdminList_WithAdminRole_ReturnsFilteredOkEnvelope()
    {
        using var factory = new AuthTestFactory();
        await SeedLegacyRefundAsync(factory, appliedPolicyId: null);
        using var client = factory.CreateApiClient();
        AuthenticateAdmin(client);

        var response = await client.GetAsync(
            "/api/admin/refunds?approveStatus=PENDING&refundStatus=PENDING&orderId=10&userId=7&refundNo=REF000401");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadEnumResponseAsync<PagedRefundResponse>(response);
        Assert.True(body.Success);
        Assert.Equal(1, body.Data!.TotalCount);
        Assert.Equal(401, Assert.Single(body.Data.Items).RefundId);
    }

    [Fact]
    public async Task AdminGet_WithAdminRole_ReturnsMappedDetail()
    {
        using var factory = new AuthTestFactory();
        await SeedLegacyRefundAsync(factory, appliedPolicyId: null);
        using var client = factory.CreateApiClient();
        AuthenticateAdmin(client);

        var response = await client.GetAsync("/api/admin/refunds/401");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadEnumResponseAsync<RefundResponse>(response);
        Assert.True(body.Success);
        Assert.Null(body.Data!.PolicyName);
    }

    [Fact]
    public async Task AdminList_WithNonAdminRole_ReturnsForbidden()
    {
        using var factory = new AuthTestFactory();
        using var client = factory.CreateApiClient();
        Authenticate(client, 7);

        var response = await client.GetAsync("/api/admin/refunds");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminReject_WithAdminRole_ReturnsOkEnvelope()
    {
        using var factory = new AuthTestFactory();
        await SeedLegacyRefundAsync(factory, appliedPolicyId: null);
        using var refundFactory = CreateRefundFactory(factory);
        using var client = CreateApiClient(refundFactory);
        AuthenticateAdmin(client);

        var response = await client.PostAsJsonAsync(
            "/api/admin/refunds/401/reject",
            new RejectRefundRequest("  资料不符合要求  "));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadEnumResponseAsync<RefundResponse>(response);
        Assert.True(body.Success);
        Assert.Equal(RefundApproveStatus.REJECTED, body.Data!.ApproveStatus);
        Assert.Equal(RefundStatus.FAILED, body.Data.RefundStatus);
        Assert.Equal("资料不符合要求", body.Data.ReviewRemark);
    }

    [Fact]
    public async Task AdminReject_WithInvalidRemark_ReturnsBadRequestEnvelope()
    {
        using var factory = new AuthTestFactory();
        await SeedLegacyRefundAsync(factory, appliedPolicyId: null);
        using var refundFactory = CreateRefundFactory(factory);
        using var client = CreateApiClient(refundFactory);
        AuthenticateAdmin(client);

        var response = await client.PostAsJsonAsync(
            "/api/admin/refunds/401/reject",
            new RejectRefundRequest("   "));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ReadEnumResponseAsync<RefundResponse>(response);
        Assert.False(body.Success);
        Assert.Equal("REFUND_REVIEW_REMARK_INVALID", body.Code);
    }

    [Fact]
    public async Task AdminApprove_WithAdminJwt_ReturnsSuccessAndPersistsAtomicWorkflow()
    {
        using var factory = new AuthTestFactory();
        await SeedLegacyRefundAsync(factory, appliedPolicyId: null);
        using var refundFactory = CreateRefundFactory(factory);
        using var client = CreateApiClient(refundFactory);
        AuthenticateAdmin(client);

        var response = await client.PostAsJsonAsync(
            "/api/admin/refunds/401/approve",
            new ApproveRefundRequest("通过"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadEnumResponseAsync<RefundResponse>(response);
        Assert.True(body.Success);
        Assert.Equal(RefundApproveStatus.APPROVED, body.Data!.ApproveStatus);
        Assert.Equal(RefundStatus.COMPLETED, body.Data.RefundStatus);
        Assert.Equal(84m, body.Data.ActualRefund);
        Assert.Equal("refund-admin", body.Data.ReviewBy);
        Assert.Equal("通过", body.Data.ReviewRemark);

        var state = await factory.ExecuteDbContextAsync(async db => new
        {
            PaymentRefundAmount = await db.Set<Payment>()
                .Where(item => item.OrderId == 10 && item.PayStatus == "SUCCESS")
                .Select(item => item.RefundAmount)
                .SingleAsync(),
            Reservation = await db.Set<SeatReservation>()
                .Where(item => item.OrderItemId == 1)
                .Select(item => new { item.ReservationStatus, item.CancelTime })
                .SingleAsync(),
            ItemStatus = await db.Set<OrderItem>()
                .Where(item => item.OrderItemId == 1)
                .Select(item => item.ItemStatus)
                .SingleAsync(),
            TicketStatus = await db.Set<ETicket>()
                .Where(item => item.OrderItemId == 1)
                .Select(item => item.TicketStatus)
                .SingleAsync(),
            OrderStatus = await db.Set<Order>()
                .Where(item => item.OrderId == 10)
                .Select(item => item.OrderStatus)
                .SingleAsync(),
        });
        Assert.Equal(84m, state.PaymentRefundAmount);
        Assert.Equal("RELEASED", state.Reservation.ReservationStatus);
        Assert.Equal(factory.UtcNow.UtcDateTime, state.Reservation.CancelTime);
        Assert.Equal("REFUNDED", state.ItemStatus);
        Assert.Equal("REFUNDED", state.TicketStatus);
        Assert.Equal("REFUNDED", state.OrderStatus);
    }

    [Fact]
    public async Task Create_WithoutAdminRole_ReturnsForbidden()
    {
        using var factory = new AuthTestFactory();
        using var client = factory.CreateApiClient();
        var token = RefundTestData.CreateToken(7, "alice", "USER");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync(
            "/api/admin/refund-policies",
            new SaveRefundPolicyRequest(null, "全局", 24, 0.8m, 0m, 1, null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_AsAdmin_ReturnsCreatedAndUsesTokenUserAsActor()
    {
        using var factory = new AuthTestFactory();
        await factory.ResetDatabaseAsync();
        using var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            RefundTestData.CreateToken(7, "policy-admin", "Admin"));

        var response = await client.PostAsJsonAsync(
            "/api/admin/refund-policies",
            new SaveRefundPolicyRequest(null, "  全局策略  ", 24, 0.8m, 0m, 1, null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var policy = await factory.ExecuteDbContextAsync(async db =>
            await db.Set<ShowtimeBackend.Entities.OrderTicket.RefundPolicy>().SingleAsync());
        Assert.Equal("全局策略", policy.PolicyName);
        Assert.Equal("policy-admin", policy.CreateBy);
    }

    [Fact]
    public async Task UpdateStatus_WithInvalidStatus_ReturnsBadRequestEnvelope()
    {
        using var factory = new AuthTestFactory();
        await factory.ResetDatabaseAsync();
        var policyId = await factory.ExecuteDbContextAsync(async db =>
        {
            var policy = new ShowtimeBackend.Entities.OrderTicket.RefundPolicy
            {
                PolicyName = "全局策略",
                RefundDeadlineHour = 24,
                RefundRate = 0.8m,
                ServiceFee = 0m,
                Priority = 1,
                Status = 1,
                CreateBy = "seed",
                UpdateBy = "seed",
            };
            db.Add(policy);
            await db.SaveChangesAsync();
            return policy.PolicyId;
        });
        using var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            RefundTestData.CreateToken(8, "admin", "Admin"));

        var response = await client.PatchAsJsonAsync(
            $"/api/admin/refund-policies/{policyId}/status",
            new UpdateRefundPolicyStatusRequest(2));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ShowtimeBackend.Common.ApiResponse<object>>();
        Assert.False(body!.Success);
        Assert.Equal("REFUND_POLICY_INVALID_STATUS", body.Code);
    }

    private static RefundRequest Refund(
        long refundId,
        long orderId,
        long userId,
        DateTime createTime) => new()
    {
        RefundId = refundId,
        RefundNo = $"REF{refundId:000000}",
        OrderId = orderId,
        UserId = userId,
        RefundType = "PART",
        RefundReason = "测试申请",
        RefundAmount = 10m,
        ActualRefund = 8m,
        FeeRate = 0.8m,
        AppliedServiceFee = 0m,
        ApproveStatus = "PENDING",
        RefundStatus = "PENDING",
        CreateTime = createTime,
    };

    private static void AssertOperationResponses(
        JsonElement paths,
        string path,
        string method,
        string successStatus,
        params string[] errorStatuses)
    {
        var responses = paths
            .GetProperty(path)
            .GetProperty(method)
            .GetProperty("responses");
        var successSchemaReference = GetJsonSchemaReference(
            responses.GetProperty(successStatus));

        foreach (var errorStatus in errorStatuses)
        {
            Assert.True(
                responses.TryGetProperty(errorStatus, out var errorResponse),
                $"{method.ToUpperInvariant()} {path} should declare {errorStatus}.");
            Assert.Equal(
                successSchemaReference,
                GetJsonSchemaReference(errorResponse));
        }
    }

    private static string GetJsonSchemaReference(JsonElement response) =>
        response
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema")
            .GetProperty("$ref")
            .GetString()
        ?? throw new InvalidOperationException("OpenAPI response schema reference is missing.");

    private static void Authenticate(HttpClient client, long userId) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            RefundTestData.CreateToken(userId, $"user-{userId}", "USER"));

    private static void AuthenticateAdmin(HttpClient client) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            RefundTestData.CreateToken(8, "refund-admin", "Admin"));

    private static WebApplicationFactory<Program> CreateRefundFactory(
        AuthTestFactory factory) => factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IRefundLockCoordinator>();
                services.AddScoped<IRefundLockCoordinator, TestRefundLockCoordinator>();
            }));

    private static HttpClient CreateApiClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

    private static async Task<ApiResponse<T>> ReadEnumResponseAsync<T>(
        HttpResponseMessage response)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return await response.Content.ReadFromJsonAsync<ApiResponse<T>>(options)
            ?? throw new InvalidOperationException("The API response body was empty.");
    }

    private static async Task SeedLegacyRefundAsync(
        AuthTestFactory factory,
        long? appliedPolicyId)
    {
        await RefundTestData.SeedIssuedOrderAsync(factory);
        await factory.ExecuteDbContextAsync(async db =>
        {
            var item = await db.Set<OrderItem>().SingleAsync(x => x.OrderItemId == 1);
            var ticket = await db.Set<ETicket>().SingleAsync(x => x.OrderItemId == 1);
            item.ItemStatus = "REFUNDING";
            ticket.TicketStatus = "REFUNDING";
            db.Add(new RefundRequest
            {
                RefundId = 401,
                RefundNo = "REF000401",
                OrderId = 10,
                UserId = 7,
                RefundType = "FULL",
                RefundReason = "历史申请",
                RefundAmount = 105m,
                ActualRefund = 84m,
                FeeRate = 0.8m,
                AppliedPolicyId = appliedPolicyId,
                AppliedServiceFee = 0m,
                ApproveStatus = "PENDING",
                RefundStatus = "PENDING",
                CreateTime = factory.UtcNow.UtcDateTime.AddHours(-1),
                Items =
                [
                    new RefundItem
                    {
                        RefundItemId = 501,
                        OrderItemId = 1,
                        RefundBaseAmount = 105m,
                    },
                ],
            });
            await db.SaveChangesAsync();
            return true;
        });
    }
}
