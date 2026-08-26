using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using ShowtimeBackend.Common;
using ShowtimeBackend.Controllers.OrderTicket;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Services.OrderTicket;
using ShowtimeBackend.Services.UserPermission;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class OrdersControllerTests
{
    [Fact]
    public async Task List_WithoutSubjectClaim_ReturnsUnauthorized()
    {
        await using var db = CreateDbContext();
        var controller = CreateController(db, new ClaimsPrincipal(new ClaimsIdentity()));

        var result = await controller.List(new OrderListQuery(null), CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task List_WithSubjectClaim_ReturnsOnlyAuthenticatedUsersOrders()
    {
        await using var db = CreateDbContext();
        db.AddRange(CreateOrder(1, 7), CreateOrder(2, 8));
        await db.SaveChangesAsync();
        var identity = new ClaimsIdentity(
            [new Claim("sub", "7"), new Claim(ClaimTypes.Name, "alice")],
            "test");
        var controller = CreateController(db, new ClaimsPrincipal(identity));

        var result = await controller.List(new OrderListQuery(null), CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result.Result);
        var apiResponse = Assert.IsType<ApiResponse<PagedOrderResponse>>(response.Value);
        var page = Assert.IsType<PagedOrderResponse>(apiResponse.Data);
        Assert.Equal(1, Assert.Single(page.Items).OrderId);
    }

    [Fact]
    public async Task Create_SuccessWritesSafeOperationLogSummary()
    {
        var response = CreateOrderResponse();
        var writer = new RecordingOperationLogWriter();
        var controller = CreateController(
            new StubOrderService(OrderTicketResult<OrderResponse>.Success(response)),
            writer);
        var request = new CreateOrderRequest(
            10,
            [new CreateOrderItemRequest(20, 30, 40, "secret-lock-token")],
            null);

        var result = await controller.Create(request, CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.NotNull(writer.Request);
        Assert.True(writer.Request.Succeeded);
        Assert.Equal("ORDER_CREATE", writer.Request.OperationType);
        var summary = JsonSerializer.Serialize(writer.Request);
        Assert.DoesNotContain("secret-lock-token", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("\"RealNameId\"", summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_BusinessFailureWritesFailedOperationLog()
    {
        var writer = new RecordingOperationLogWriter();
        var controller = CreateController(
            new StubOrderService(OrderTicketResult<OrderResponse>.Fail(
                OrderTicketFailure.Conflict,
                "ORDER_SEAT_LOCK_INVALID",
                "The seat lock is invalid.")),
            writer);
        var request = new CreateOrderRequest(
            10,
            [new CreateOrderItemRequest(20, 30, null, "secret-lock-token")],
            null);

        var result = await controller.Create(request, CancellationToken.None);

        var failure = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, failure.StatusCode);
        Assert.NotNull(writer.Request);
        Assert.False(writer.Request.Succeeded);
        Assert.Equal("ORDER_SEAT_LOCK_INVALID", writer.Request.ErrorMessage);
    }

    [Fact]
    public async Task Create_OperationLogFailureDoesNotChangeBusinessResponse()
    {
        var controller = CreateController(
            new StubOrderService(OrderTicketResult<OrderResponse>.Success(CreateOrderResponse())),
            new ThrowingOperationLogWriter());

        var result = await controller.Create(
            new CreateOrderRequest(10, [], null),
            CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    private static OrdersController CreateController(AppDbContext db, ClaimsPrincipal user)
    {
        var controller = new OrdersController(
            new OrderService(db, TimeProvider.System),
            new NullOperationLogWriter(),
            TimeProvider.System)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            }
        };
        return controller;
    }

    private static OrdersController CreateController(
        IOrderService orderService,
        IOperationLogWriter writer)
    {
        var identity = new ClaimsIdentity(
            [new Claim("sub", "7"), new Claim(ClaimTypes.Name, "alice")],
            "test");
        return new OrdersController(orderService, writer, TimeProvider.System)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(identity),
                },
            },
        };
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static Order CreateOrder(long orderId, long userId) => new()
    {
        OrderId = orderId,
        OrderNo = $"ORD{orderId:000000}",
        UserId = userId,
        SessionId = 10,
        TotalAmount = 188m,
        TicketCount = 1,
        OrderStatus = "PENDING_PAY",
        ExpireTime = DateTime.UtcNow.AddMinutes(15),
        Source = "WEB"
    };

    private sealed class NullOperationLogWriter : IOperationLogWriter
    {
        public ValueTask WriteAsync(
            OperationLogWriteRequest request,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class RecordingOperationLogWriter : IOperationLogWriter
    {
        public OperationLogWriteRequest? Request { get; private set; }

        public ValueTask WriteAsync(
            OperationLogWriteRequest request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingOperationLogWriter : IOperationLogWriter
    {
        public ValueTask WriteAsync(
            OperationLogWriteRequest request,
            CancellationToken cancellationToken) =>
            ValueTask.FromException(new InvalidOperationException("audit unavailable"));
    }

    private sealed class StubOrderService(
        OrderTicketResult<OrderResponse> createResult) : IOrderService
    {
        public Task<OrderTicketResult<OrderResponse>> CreateAsync(
            long userId,
            string actor,
            CreateOrderRequest request,
            CancellationToken cancellationToken) => Task.FromResult(createResult);

        public Task<OrderTicketResult<PagedOrderResponse>> ListAsync(
            long userId,
            OrderListQuery query,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<OrderTicketResult<OrderResponse>> GetAsync(
            long userId,
            long orderId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<OrderTicketResult<OrderResponse>> CancelAsync(
            long userId,
            string actor,
            long orderId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<OrderTicketResult<PagedAdminOrderResponse>> ListAdminAsync(
            AdminOrderListQuery query,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<OrderTicketResult<OrderResponse>> GetAdminAsync(
            long orderId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<OrderTicketResult<OrderResponse>> CancelAdminAsync(
            string actor,
            long orderId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private static OrderResponse CreateOrderResponse() => new(
        100,
        "ORD000100",
        10,
        188,
        0,
        1,
        OrderStatus.PENDING_PAY,
        DateTime.UtcNow.AddMinutes(15),
        null,
        null,
        null,
        "WEB",
        null,
        [],
        [],
        [],
        DateTime.UtcNow);
}
