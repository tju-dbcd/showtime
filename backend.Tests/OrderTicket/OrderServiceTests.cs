using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Entities.SeatZone;
using ShowtimeBackend.Entities.ShowSession;
using ShowtimeBackend.Entities.UserPermission;
using ShowtimeBackend.Services.OrderTicket;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class OrderServiceTests
{
    [Fact]
    public async Task CreateAsync_ComputesAmountsFromEnabledPriceStrategies()
    {
        await using var db = CreateDbContext();
        db.AddRange(
            new ShowSession { SessionId = 10, ShowId = 20, SeatMapId = 30 },
            new SeatSection { SeatSectionId = 40, SeatMapId = 30, SectionCode = "A", SectionName = "A区" },
            new Seat { SeatId = 50, SeatSectionId = 40, RowCode = "1", SeatNo = "1", IsSellable = true, SeatStatus = "ENABLED" },
            new Seat { SeatId = 51, SeatSectionId = 40, RowCode = "1", SeatNo = "2", IsSellable = true, SeatStatus = "ENABLED" },
            new PriceStrategy { PriceStrategyId = 60, SessionId = 10, SeatSectionId = 40, Price = 188m, Status = "ENABLED" });
        await db.SaveChangesAsync();
        var service = new OrderService(db, new FixedTimeProvider(new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero)));
        var request = new CreateOrderRequest(
            10,
            [new CreateOrderItemRequest(50, 60, null), new CreateOrderItemRequest(51, 60, null)],
            "靠近过道");

        var result = await service.CreateAsync(7, "alice", request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(376m, result.Value!.TotalAmount);
        Assert.Equal(0m, result.Value.DiscountAmount);
        Assert.Equal(2, result.Value.TicketCount);
        Assert.Equal("PENDING_PAY", result.Value.OrderStatus);
        Assert.Equal(new DateTime(2026, 8, 2, 12, 15, 0), result.Value.ExpireTime);
        Assert.Equal([188m, 188m], result.Value.Items.Select(item => item.UnitPrice));
    }

    [Fact]
    public async Task CreateAsync_RejectsDuplicateSeats()
    {
        await using var db = CreateDbContext();
        await SeedCatalogAsync(db);
        var service = new OrderService(db, TimeProvider.System);
        var request = new CreateOrderRequest(
            10,
            [new CreateOrderItemRequest(50, 60, null), new CreateOrderItemRequest(50, 60, null)],
            null);

        var result = await service.CreateAsync(7, "alice", request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("ORDER_INVALID_ITEMS", result.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_RejectsUnavailableSeat()
    {
        await using var db = CreateDbContext();
        await SeedCatalogAsync(db, seatIsSellable: false);
        var service = new OrderService(db, TimeProvider.System);

        var result = await service.CreateAsync(
            7,
            "alice",
            new CreateOrderRequest(10, [new CreateOrderItemRequest(50, 60, null)], null),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("ORDER_SEAT_UNAVAILABLE", result.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_RejectsPriceStrategyForAnotherSession()
    {
        await using var db = CreateDbContext();
        await SeedCatalogAsync(db, priceSessionId: 11);
        var service = new OrderService(db, TimeProvider.System);

        var result = await service.CreateAsync(
            7,
            "alice",
            new CreateOrderRequest(10, [new CreateOrderItemRequest(50, 60, null)], null),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("ORDER_INVALID_PRICE_STRATEGY", result.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_RejectsRealNameOwnedByAnotherUser()
    {
        await using var db = CreateDbContext();
        await SeedCatalogAsync(db);
        db.Add(new UserRealName
        {
            RealNameId = 70,
            UserId = 8,
            RealName = "Bob",
            IdCardNo = "120000000000000000",
            IsVerified = true
        });
        await db.SaveChangesAsync();
        var service = new OrderService(db, TimeProvider.System);

        var result = await service.CreateAsync(
            7,
            "alice",
            new CreateOrderRequest(10, [new CreateOrderItemRequest(50, 60, 70)], null),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("ORDER_INVALID_REAL_NAME", result.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_RejectsMissingSession()
    {
        await using var db = CreateDbContext();
        db.AddRange(
            new SeatSection { SeatSectionId = 40, SeatMapId = 30, SectionCode = "A", SectionName = "A区" },
            new Seat { SeatId = 50, SeatSectionId = 40, RowCode = "1", SeatNo = "1", IsSellable = true, SeatStatus = "ENABLED" },
            new PriceStrategy { PriceStrategyId = 60, SessionId = 10, SeatSectionId = 40, Price = 188m, Status = "ENABLED" });
        await db.SaveChangesAsync();
        var service = new OrderService(db, TimeProvider.System);

        var result = await service.CreateAsync(
            7,
            "alice",
            new CreateOrderRequest(10, [new CreateOrderItemRequest(50, 60, null)], null),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("ORDER_SESSION_NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public async Task ListAsync_ReturnsOnlyCurrentUsersFilteredOrders()
    {
        await using var db = CreateDbContext();
        db.AddRange(
            CreateOrder(1, 7, "PENDING_PAY", new DateTime(2026, 8, 2, 10, 0, 0)),
            CreateOrder(2, 7, "PAID", new DateTime(2026, 8, 2, 11, 0, 0)),
            CreateOrder(3, 8, "PENDING_PAY", new DateTime(2026, 8, 2, 12, 0, 0)));
        await db.SaveChangesAsync();
        var service = new OrderService(db, TimeProvider.System);

        var result = await service.ListAsync(7, new OrderListQuery("PENDING_PAY", 1, 20), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var order = Assert.Single(result.Value!.Items);
        Assert.Equal(1, order.OrderId);
        Assert.Equal(1, result.Value.TotalCount);
    }

    [Fact]
    public async Task GetAsync_DoesNotExposeAnotherUsersOrder()
    {
        await using var db = CreateDbContext();
        db.Add(CreateOrder(1, 8, "PENDING_PAY", new DateTime(2026, 8, 2, 10, 0, 0)));
        await db.SaveChangesAsync();
        var service = new OrderService(db, TimeProvider.System);

        var result = await service.GetAsync(7, 1, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderTicketFailure.NotFound, result.Failure);
        Assert.Equal("ORDER_NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public async Task GetAsync_ReturnsItemsPaymentsAndExistingTickets()
    {
        await using var db = CreateDbContext();
        var order = CreateOrder(1, 7, "PAID", new DateTime(2026, 8, 2, 10, 0, 0));
        var item = new OrderItem
        {
            OrderItemId = 2,
            SeatId = 50,
            PriceStrategyId = 60,
            UnitPrice = 188m,
            ItemStatus = "NORMAL"
        };
        item.ETicket = new ETicket
        {
            ETicketId = 3,
            ETicketNo = "TICKET000003",
            UserId = 7,
            QrCode = "existing-qr",
            AntiFakeCode = "existing-code",
            TicketStatus = "UNUSED"
        };
        order.Items.Add(item);
        order.Payments.Add(new Payment
        {
            PaymentId = 4,
            PaymentNo = "PAY000004",
            UserId = 7,
            PayAmount = 188m,
            PayChannel = "ALIPAY",
            PayStatus = "SUCCESS"
        });
        db.Add(order);
        await db.SaveChangesAsync();
        var service = new OrderService(db, TimeProvider.System);

        var result = await service.GetAsync(7, 1, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(4, Assert.Single(result.Value!.Payments).PaymentId);
        Assert.Equal("TICKET000003", Assert.Single(result.Value.Tickets).ETicketNo);
    }

    [Fact]
    public async Task CancelAsync_CancelsPendingOrder()
    {
        await using var db = CreateDbContext();
        db.Add(CreateOrder(1, 7, "PENDING_PAY", new DateTime(2026, 8, 2, 10, 0, 0)));
        await db.SaveChangesAsync();
        var now = new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
        var service = new OrderService(db, new FixedTimeProvider(now));

        var result = await service.CancelAsync(7, "alice", 1, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("CANCELLED", result.Value!.OrderStatus);
        Assert.Equal(now.UtcDateTime, result.Value.CancelTime);
    }

    [Fact]
    public async Task CancelAsync_RejectsPaidOrder()
    {
        await using var db = CreateDbContext();
        db.Add(CreateOrder(1, 7, "PAID", new DateTime(2026, 8, 2, 10, 0, 0)));
        await db.SaveChangesAsync();
        var service = new OrderService(db, TimeProvider.System);

        var result = await service.CancelAsync(7, "alice", 1, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderTicketFailure.Conflict, result.Failure);
        Assert.Equal("ORDER_CANNOT_CANCEL", result.ErrorCode);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static Order CreateOrder(long orderId, long userId, string status, DateTime createTime) => new()
    {
        OrderId = orderId,
        OrderNo = $"ORD{orderId:000000}",
        UserId = userId,
        SessionId = 10,
        TotalAmount = 188m,
        DiscountAmount = 0m,
        TicketCount = 1,
        OrderStatus = status,
        ExpireTime = createTime.AddMinutes(15),
        Source = "WEB",
        CreateTime = createTime,
        UpdateTime = createTime
    };

    private static async Task SeedCatalogAsync(
        AppDbContext db,
        bool seatIsSellable = true,
        long priceSessionId = 10)
    {
        db.AddRange(
            new ShowSession { SessionId = 10, ShowId = 20, SeatMapId = 30 },
            new SeatSection { SeatSectionId = 40, SeatMapId = 30, SectionCode = "A", SectionName = "A区" },
            new Seat
            {
                SeatId = 50,
                SeatSectionId = 40,
                RowCode = "1",
                SeatNo = "1",
                IsSellable = seatIsSellable,
                SeatStatus = "ENABLED"
            },
            new PriceStrategy
            {
                PriceStrategyId = 60,
                SessionId = priceSessionId,
                SeatSectionId = 40,
                Price = 188m,
                Status = "ENABLED"
            });
        await db.SaveChangesAsync();
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
