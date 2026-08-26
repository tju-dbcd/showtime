using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Common;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Services.OrderTicket;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class TicketQueryServiceTests
{
    [Fact]
    public async Task ListForOwnerAsync_ReturnsOwnersTicketsOrderedByOrderItemId()
    {
        await using var db = CreateDbContext();
        var order = CreateOrder(orderId: 10, userId: 7);
        order.Items.Add(CreateItemWithTicket(order, orderItemId: 2, ticketId: 102));
        order.Items.Add(CreateItemWithTicket(order, orderItemId: 1, ticketId: 101));
        db.Add(order);
        await db.SaveChangesAsync();
        var service = new TicketQueryService(db);

        var result = await service.ListForOwnerAsync(7, 10, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var tickets = Assert.IsAssignableFrom<IReadOnlyList<TicketResponse>>(
            result.Value);
        Assert.Equal([1L, 2L], tickets.Select(ticket => ticket.OrderItemId));
        Assert.All(tickets, ticket =>
        {
            Assert.Equal(ETicketStatus.UNUSED, ticket.TicketStatus);
            Assert.StartsWith("qr-", ticket.QrCode);
        });
    }

    [Theory]
    [InlineData(8, 10)]
    [InlineData(7, 999)]
    public async Task ListForOwnerAsync_HidesOrdersNotOwnedByCurrentUser(
        long userId,
        long orderId)
    {
        await using var db = CreateDbContext();
        db.Add(CreateOrder(orderId: 10, userId: 7));
        await db.SaveChangesAsync();
        var service = new TicketQueryService(db);

        var result = await service.ListForOwnerAsync(
            userId,
            orderId,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderTicketFailure.NotFound, result.Failure);
        Assert.Equal("TICKET_ORDER_NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public async Task ListForOwnerAsync_ReturnsEmptyListBeforeTicketsExist()
    {
        await using var db = CreateDbContext();
        var order = CreateOrder(orderId: 10, userId: 7);
        order.OrderStatus = "PENDING_PAY";
        order.IssueTime = null;
        db.Add(order);
        await db.SaveChangesAsync();
        var service = new TicketQueryService(db);

        var result = await service.ListForOwnerAsync(7, 10, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
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
        SessionId = 20,
        TotalAmount = 376m,
        TicketCount = 2,
        OrderStatus = "ISSUED",
        ExpireTime = DateTime.UtcNow.AddMinutes(15),
        IssueTime = DateTime.UtcNow,
        Source = "WEB",
    };

    private static OrderItem CreateItemWithTicket(
        Order order,
        long orderItemId,
        long ticketId)
    {
        var item = new OrderItem
        {
            OrderItemId = orderItemId,
            OrderId = order.OrderId,
            SeatId = 100 + orderItemId,
            PriceStrategyId = 200,
            UnitPrice = 188m,
            ItemStatus = "NORMAL",
            Order = order,
        };
        item.ETicket = new ETicket
        {
            ETicketId = ticketId,
            ETicketNo = $"TKT{ticketId}",
            OrderItemId = orderItemId,
            UserId = order.UserId,
            QrCode = $"qr-{ticketId}",
            AntiFakeCode = $"anti-{ticketId}",
            TicketStatus = "UNUSED",
            OrderItem = item,
        };
        return item;
    }
}
