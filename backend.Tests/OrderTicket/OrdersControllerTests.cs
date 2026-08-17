using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Common;
using ShowtimeBackend.Controllers.OrderTicket;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Services.OrderTicket;

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

    private static OrdersController CreateController(AppDbContext db, ClaimsPrincipal user)
    {
        var controller = new OrdersController(new OrderService(db, TimeProvider.System))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            }
        };
        return controller;
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
}
