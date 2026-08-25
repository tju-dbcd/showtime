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

public sealed class OrderTicketsControllerTests
{
    [Fact]
    public async Task List_ForAuthenticatedOwner_ReturnsPrivateNoStoreResponse()
    {
        await using var db = CreateDbContext();
        db.Add(CreateIssuedOrderWithTicket());
        await db.SaveChangesAsync();
        var identity = new ClaimsIdentity(
            [new Claim("sub", "7"), new Claim(ClaimTypes.Name, "alice")],
            "test");
        var controller = CreateController(db, new ClaimsPrincipal(identity));

        var result = await controller.List(10, CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<IReadOnlyList<TicketResponse>>>(
            response.Value);
        Assert.Single(envelope.Data!);
        Assert.Equal(
            "private, no-store",
            controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task List_WithoutSubjectClaim_ReturnsUnauthorized()
    {
        await using var db = CreateDbContext();
        var controller = CreateController(
            db,
            new ClaimsPrincipal(new ClaimsIdentity()));

        var result = await controller.List(10, CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    private static OrderTicketsController CreateController(
        AppDbContext db,
        ClaimsPrincipal user) => new(new TicketQueryService(db))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user },
            },
        };

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static Order CreateIssuedOrderWithTicket()
    {
        var order = new Order
        {
            OrderId = 10,
            OrderNo = "ORD000010",
            UserId = 7,
            SessionId = 20,
            TotalAmount = 188m,
            TicketCount = 1,
            OrderStatus = "ISSUED",
            ExpireTime = DateTime.UtcNow.AddMinutes(15),
            IssueTime = DateTime.UtcNow,
            Source = "WEB",
        };
        var item = new OrderItem
        {
            OrderItemId = 1,
            OrderId = order.OrderId,
            SeatId = 100,
            PriceStrategyId = 200,
            UnitPrice = 188m,
            ItemStatus = "NORMAL",
            Order = order,
        };
        item.ETicket = new ETicket
        {
            ETicketId = 101,
            ETicketNo = "TKT101",
            OrderItemId = item.OrderItemId,
            UserId = order.UserId,
            QrCode = "qr-101",
            AntiFakeCode = "anti-101",
            TicketStatus = "UNUSED",
            OrderItem = item,
        };
        order.Items.Add(item);
        return order;
    }
}
