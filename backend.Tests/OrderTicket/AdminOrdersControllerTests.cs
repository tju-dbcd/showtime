using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Common;
using ShowtimeBackend.Controllers.OrderTicket;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Entities.UserPermission;
using ShowtimeBackend.Services.OrderTicket;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class AdminOrdersControllerTests
{
    [Fact]
    public void Controller_RequiresAdminRoleAtAdminOrdersRoute()
    {
        var controllerType = typeof(AdminOrdersController);

        var route = controllerType.GetCustomAttribute<RouteAttribute>();
        var authorize = controllerType.GetCustomAttribute<AuthorizeAttribute>();

        Assert.Equal("api/admin/orders", route!.Template);
        Assert.Equal("Admin", authorize!.Roles);
    }

    [Fact]
    public async Task List_ReturnsOrdersAcrossUsers()
    {
        await using var db = CreateDbContext();
        db.AddRange(
            CreateUser(7, "alice", "13800000001"),
            CreateUser(8, "bob", "13800000002"),
            CreateOrder(1, 7),
            CreateOrder(2, 8));
        await db.SaveChangesAsync();
        var controller = CreateController(db);

        var result = await controller.List(
            new AdminOrderListQuery(null, null),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<PagedAdminOrderResponse>>(ok.Value);
        Assert.Equal(2, response.Data!.TotalCount);
    }

    [Fact]
    public async Task Get_ReturnsOrderOwnedByAnotherUser()
    {
        await using var db = CreateDbContext();
        db.Add(CreateOrder(1, 8));
        await db.SaveChangesAsync();
        var controller = CreateController(db);

        var result = await controller.Get(1, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<OrderResponse>>(ok.Value);
        Assert.Equal(1, response.Data!.OrderId);
    }

    [Fact]
    public async Task Cancel_CancelsPendingOrderAsCurrentAdmin()
    {
        await using var db = CreateDbContext();
        db.Add(CreateOrder(1, 8));
        await db.SaveChangesAsync();
        var controller = CreateController(db);

        var result = await controller.Cancel(1, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        var order = await db.Set<Order>().SingleAsync();
        Assert.Equal("CANCELLED", order.OrderStatus);
        Assert.Equal("admin-user", order.UpdateBy);
    }

    [Fact]
    public async Task Cancel_ReturnsConflictForPaidOrder()
    {
        await using var db = CreateDbContext();
        db.Add(CreateOrder(1, 8, "PAID"));
        await db.SaveChangesAsync();
        var controller = CreateController(db);

        var result = await controller.Cancel(1, CancellationToken.None);

        var conflict = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        var response = Assert.IsType<ApiResponse<OrderResponse>>(conflict.Value);
        Assert.Equal("ORDER_CANNOT_CANCEL", response.Code);
    }

    [Fact]
    public async Task Issue_UsesCurrentAdminAndReturnsCompensationResult()
    {
        await using var db = CreateDbContext();
        var issuance = new StubAdminTicketIssuanceService(
            OrderTicketResult<TicketIssuanceResponse>.Success(
                new TicketIssuanceResponse(
                    21,
                    OrderStatus.ISSUED,
                    2,
                    0,
                    2,
                    new DateTime(2026, 8, 23, 10, 0, 0))));
        var controller = CreateController(db, issuance);

        var result = await controller.Issue(21, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<TicketIssuanceResponse>>(ok.Value);
        Assert.Equal(21, response.Data!.OrderId);
        Assert.Equal("admin-user", issuance.Actor);
        Assert.Equal(21, issuance.OrderId);
    }

    private static AdminOrdersController CreateController(
        AppDbContext db,
        IAdminTicketIssuanceService? issuanceService = null)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim("sub", "1001"),
                new Claim(ClaimTypes.Name, "admin-user"),
                new Claim(ClaimTypes.Role, "Admin")
            ],
            "test");
        return new AdminOrdersController(
            new OrderService(db, TimeProvider.System),
            issuanceService ?? new StubAdminTicketIssuanceService(
                OrderTicketResult<TicketIssuanceResponse>.Fail(
                    OrderTicketFailure.NotFound,
                    "TICKET_ORDER_NOT_FOUND",
                    "The order does not exist.")))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(identity)
                }
            }
        };
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static SysUser CreateUser(long userId, string userName, string phone) => new()
    {
        UserId = userId,
        UserName = userName,
        PasswordHash = "test-password-hash",
        Phone = phone
    };

    private static Order CreateOrder(long orderId, long userId, string status = "PENDING_PAY") => new()
    {
        OrderId = orderId,
        OrderNo = $"ORD{orderId:000000}",
        UserId = userId,
        SessionId = 10,
        TotalAmount = 188m,
        TicketCount = 1,
        OrderStatus = status,
        ExpireTime = DateTime.UtcNow.AddMinutes(15),
        Source = "WEB"
    };

    private sealed class StubAdminTicketIssuanceService(
        OrderTicketResult<TicketIssuanceResponse> result)
        : IAdminTicketIssuanceService
    {
        public string? Actor { get; private set; }
        public long? OrderId { get; private set; }

        public Task<OrderTicketResult<TicketIssuanceResponse>> IssueAsync(
            string actor,
            long orderId,
            CancellationToken cancellationToken)
        {
            Actor = actor;
            OrderId = orderId;
            return Task.FromResult(result);
        }
    }
}
