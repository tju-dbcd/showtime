using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Common;
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
        var now = new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
        await AddActiveLockAsync(db, 50, "lock-50", now.UtcDateTime);
        await AddActiveLockAsync(db, 51, "lock-51", now.UtcDateTime);
        var service = new OrderService(db, new FixedTimeProvider(now));
        var request = new CreateOrderRequest(
            10,
            [
                new CreateOrderItemRequest(50, 60, null, "lock-50"),
                new CreateOrderItemRequest(51, 60, null, "lock-51")
            ],
            "靠近过道");

        var result = await service.CreateAsync(7, "alice", request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(376m, result.Value!.TotalAmount);
        Assert.Equal(0m, result.Value.DiscountAmount);
        Assert.Equal(2, result.Value.TicketCount);
        Assert.Equal(OrderStatus.PENDING_PAY, result.Value.OrderStatus);
        Assert.Equal(new DateTime(2026, 8, 2, 12, 15, 0), result.Value.ExpireTime);
        Assert.Equal([188m, 188m], result.Value.Items.Select(item => item.UnitPrice));
        Assert.All(await db.SeatLocks.ToListAsync(), item =>
            Assert.Equal("CONVERTED", item.LockStatus));
        var reservations = await db.SeatReservations
            .OrderBy(item => item.SeatId)
            .ToListAsync();
        Assert.Equal(2, reservations.Count);
        Assert.All(reservations, item =>
        {
            Assert.Equal("ORDER", item.ReservationType);
            Assert.Equal("ACTIVE", item.ReservationStatus);
            Assert.NotNull(item.OrderItemId);
            Assert.NotNull(item.SeatLockId);
        });
    }

    [Fact]
    public async Task CreateAsync_RejectsOrderWithoutSeatLock()
    {
        await using var db = CreateDbContext();
        await SeedCatalogAsync(db);
        var now = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
        var service = new OrderService(db, new FixedTimeProvider(now));

        var result = await service.CreateAsync(
            7,
            "alice",
            new CreateOrderRequest(
                10,
                [new CreateOrderItemRequest(50, 60, null, "missing-lock")],
                null),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderTicketFailure.Conflict, result.Failure);
        Assert.Equal("ORDER_SEAT_LOCK_INVALID", result.ErrorCode);
        Assert.Empty(await db.Set<Order>().ToListAsync());
        Assert.Empty(await db.SeatReservations.ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_RejectsLockTokenLongerThanDatabaseColumn()
    {
        await using var db = CreateDbContext();
        await SeedCatalogAsync(db);
        var service = new OrderService(db, TimeProvider.System);

        var result = await service.CreateAsync(
            7,
            "alice",
            new CreateOrderRequest(
                10,
                [new CreateOrderItemRequest(50, 60, null, new string('a', 65))],
                null),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderTicketFailure.InvalidRequest, result.Failure);
        Assert.Equal("ORDER_INVALID_ITEMS", result.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_RejectsMoreThanNineHundredNinetyNineSeats()
    {
        await using var db = CreateDbContext();
        var service = new OrderService(db, TimeProvider.System);
        var items = Enumerable.Range(1, 1_000)
            .Select(value => new CreateOrderItemRequest(
                value,
                value,
                null,
                $"lock-{value}"))
            .ToArray();

        var result = await service.CreateAsync(
            7,
            "alice",
            new CreateOrderRequest(10, items, null),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderTicketFailure.InvalidRequest, result.Failure);
        Assert.Equal("ORDER_INVALID_ITEMS", result.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_RejectsWrongSeatLockToken()
    {
        await using var db = CreateDbContext();
        await SeedCatalogAsync(db);
        var now = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
        await AddActiveLockAsync(db, 50, "server-token", now.UtcDateTime);
        var service = new OrderService(db, new FixedTimeProvider(now));

        var result = await service.CreateAsync(
            7,
            "alice",
            new CreateOrderRequest(
                10,
                [new CreateOrderItemRequest(50, 60, null, "wrong-token")],
                null),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderTicketFailure.Conflict, result.Failure);
        Assert.Equal("ORDER_SEAT_LOCK_INVALID", result.ErrorCode);
        Assert.Empty(await db.Set<Order>().ToListAsync());
        Assert.Empty(await db.SeatReservations.ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_RejectsExpiredSeatLock()
    {
        await using var db = CreateDbContext();
        await SeedCatalogAsync(db);
        var now = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
        db.Add(new SeatLock
        {
            SessionId = 10,
            SeatId = 50,
            UserId = 7,
            LockToken = "expired-token",
            LockStatus = "ACTIVE",
            LockTime = now.UtcDateTime.AddMinutes(-10),
            ExpireTime = now.UtcDateTime
        });
        await db.SaveChangesAsync();
        var service = new OrderService(db, new FixedTimeProvider(now));

        var result = await service.CreateAsync(
            7,
            "alice",
            new CreateOrderRequest(
                10,
                [new CreateOrderItemRequest(50, 60, null, "expired-token")],
                null),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderTicketFailure.Conflict, result.Failure);
        Assert.Equal("ORDER_SEAT_LOCK_INVALID", result.ErrorCode);
        Assert.Empty(await db.Set<Order>().ToListAsync());
        Assert.Empty(await db.SeatReservations.ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_RejectsDuplicateSeats()
    {
        await using var db = CreateDbContext();
        await SeedCatalogAsync(db);
        var service = new OrderService(db, TimeProvider.System);
        var request = new CreateOrderRequest(
            10,
            [
                new CreateOrderItemRequest(50, 60, null, "test-lock-1"),
                new CreateOrderItemRequest(50, 60, null, "test-lock-2")
            ],
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
            new CreateOrderRequest(10, [new CreateOrderItemRequest(50, 60, null, "test-lock")], null),
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
            new CreateOrderRequest(10, [new CreateOrderItemRequest(50, 60, null, "test-lock")], null),
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
            new CreateOrderRequest(10, [new CreateOrderItemRequest(50, 60, 70, "test-lock")], null),
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
            new CreateOrderRequest(10, [new CreateOrderItemRequest(50, 60, null, "test-lock")], null),
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

        var result = await service.ListAsync(7, new OrderListQuery(OrderStatus.PENDING_PAY, 1, 20), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var order = Assert.Single(result.Value!.Items);
        Assert.Equal(1, order.OrderId);
        Assert.Equal(1, result.Value.TotalCount);
    }

    [Fact]
    public async Task ListAdminAsync_ReturnsOrdersAcrossUsersWithUserInformation()
    {
        await using var db = CreateDbContext();
        db.AddRange(
            CreateUser(7, "alice", "Alice", "13800000001"),
            CreateUser(8, "bob", "Bob", "13800000002"),
            CreateOrder(1, 7, "PENDING_PAY", new DateTime(2026, 8, 2, 10, 0, 0)),
            CreateOrder(2, 8, "PAID", new DateTime(2026, 8, 2, 11, 0, 0)));
        await db.SaveChangesAsync();
        var service = new OrderService(db, TimeProvider.System);

        var result = await service.ListAdminAsync(
            new AdminOrderListQuery(null, null, 1, 20),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.TotalCount);
        Assert.Equal([2L, 1L], result.Value.Items.Select(item => item.OrderId));
        Assert.Equal("bob", result.Value.Items[0].UserName);
        Assert.Equal("Alice", result.Value.Items[1].Nickname);
    }

    [Theory]
    [InlineData("PAID", 2)]
    [InlineData("ORD000001", 1)]
    [InlineData("alice", 1)]
    [InlineData("Alice", 1)]
    [InlineData("13800000001", 1)]
    public async Task ListAdminAsync_FiltersByStatusOrKeyword(string filter, long expectedOrderId)
    {
        await using var db = CreateDbContext();
        db.AddRange(
            CreateUser(7, "alice", "Alice", "13800000001"),
            CreateUser(8, "bob", "Bob", "13800000002"),
            CreateOrder(1, 7, "PENDING_PAY", new DateTime(2026, 8, 2, 10, 0, 0)),
            CreateOrder(2, 8, "PAID", new DateTime(2026, 8, 2, 11, 0, 0)));
        await db.SaveChangesAsync();
        var service = new OrderService(db, TimeProvider.System);
        var query = filter == "PAID"
            ? new AdminOrderListQuery(OrderStatus.PAID, null, 1, 20)
            : new AdminOrderListQuery(null, filter, 1, 20);

        var result = await service.ListAdminAsync(query, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var order = Assert.Single(result.Value!.Items);
        Assert.Equal(expectedOrderId, order.OrderId);
    }

    [Fact]
    public async Task ListAdminAsync_RejectsInvalidPaging()
    {
        await using var db = CreateDbContext();
        var service = new OrderService(db, TimeProvider.System);

        var result = await service.ListAdminAsync(
            new AdminOrderListQuery(null, null, 0, 20),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderTicketFailure.InvalidRequest, result.Failure);
        Assert.Equal("ORDER_INVALID_PAGING", result.ErrorCode);
    }

    [Fact]
    public async Task ListAdminAsync_RejectsPagingOffsetOverflow()
    {
        await using var db = CreateDbContext();
        var service = new OrderService(db, TimeProvider.System);

        var result = await service.ListAdminAsync(
            new AdminOrderListQuery(null, null, int.MaxValue, 100),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderTicketFailure.InvalidRequest, result.Failure);
        Assert.Equal("ORDER_INVALID_PAGING", result.ErrorCode);
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
        order.IssueTime = new DateTime(2026, 8, 2, 10, 5, 0);
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
        Assert.Equal(order.IssueTime, result.Value!.IssueTime);
        Assert.Equal(4, Assert.Single(result.Value.Payments).PaymentId);
        Assert.Equal("TICKET000003", Assert.Single(result.Value.Tickets).ETicketNo);
    }

    [Fact]
    public async Task GetAdminAsync_ReturnsAnotherUsersOrder()
    {
        await using var db = CreateDbContext();
        db.Add(CreateOrder(1, 8, "PENDING_PAY", new DateTime(2026, 8, 2, 10, 0, 0)));
        await db.SaveChangesAsync();
        var service = new OrderService(db, TimeProvider.System);

        var result = await service.GetAdminAsync(1, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.OrderId);
    }

    [Fact]
    public async Task CancelAsync_CancelsPendingOrderAndReservation()
    {
        await using var db = CreateDbContext();
        var order = CreateOrder(1, 7, "PENDING_PAY", new DateTime(2026, 8, 2, 10, 0, 0));
        order.Items.Add(new OrderItem
        {
            OrderItemId = 2,
            SeatId = 50,
            PriceStrategyId = 60,
            UnitPrice = 188m,
            ItemStatus = "NORMAL"
        });
        db.AddRange(
            order,
            new SeatReservation
            {
                SeatReservationId = 3,
                SessionId = 10,
                SeatId = 50,
                OrderItemId = 2,
                ReservationType = "ORDER",
                ReservationStatus = "ACTIVE",
                ReserveTime = new DateTime(2026, 8, 2, 10, 0, 0)
            });
        await db.SaveChangesAsync();
        var now = new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
        var service = new OrderService(db, new FixedTimeProvider(now));

        var result = await service.CancelAsync(7, "alice", 1, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.CANCELLED, result.Value!.OrderStatus);
        Assert.Equal(now.UtcDateTime, result.Value.CancelTime);
        var reservation = Assert.Single(await db.SeatReservations.ToListAsync());
        Assert.Equal("CANCELLED", reservation.ReservationStatus);
        Assert.Equal(now.UtcDateTime, reservation.CancelTime);
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

    [Fact]
    public async Task CancelAdminAsync_CancelsPendingOrderAndReservationAcrossUsers()
    {
        await using var db = CreateDbContext();
        var order = CreateOrder(1, 8, "PENDING_PAY", new DateTime(2026, 8, 2, 10, 0, 0));
        order.Items.Add(new OrderItem
        {
            OrderItemId = 2,
            SeatId = 50,
            PriceStrategyId = 60,
            UnitPrice = 188m,
            ItemStatus = "NORMAL"
        });
        db.AddRange(
            order,
            new SeatReservation
            {
                SeatReservationId = 3,
                SessionId = 10,
                SeatId = 50,
                OrderItemId = 2,
                ReservationType = "ORDER",
                ReservationStatus = "ACTIVE",
                ReserveTime = new DateTime(2026, 8, 2, 10, 0, 0)
            });
        await db.SaveChangesAsync();
        var now = new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
        var service = new OrderService(db, new FixedTimeProvider(now));

        var result = await service.CancelAdminAsync("admin", 1, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.CANCELLED, result.Value!.OrderStatus);
        Assert.Equal("admin", (await db.Set<Order>().SingleAsync()).UpdateBy);
        Assert.Equal("CANCELLED", (await db.SeatReservations.SingleAsync()).ReservationStatus);
    }

    [Fact]
    public async Task CancelAdminAsync_RejectsPaidOrderWithoutChangingIt()
    {
        await using var db = CreateDbContext();
        db.Add(CreateOrder(1, 8, "PAID", new DateTime(2026, 8, 2, 10, 0, 0)));
        await db.SaveChangesAsync();
        var service = new OrderService(db, TimeProvider.System);

        var result = await service.CancelAdminAsync("admin", 1, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderTicketFailure.Conflict, result.Failure);
        Assert.Equal("ORDER_CANNOT_CANCEL", result.ErrorCode);
        Assert.Equal("PAID", (await db.Set<Order>().SingleAsync()).OrderStatus);
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

    private static SysUser CreateUser(
        long userId,
        string userName,
        string nickname,
        string phone) => new()
        {
            UserId = userId,
            UserName = userName,
            PasswordHash = "test-password-hash",
            Nickname = nickname,
            Phone = phone
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

    private static async Task AddActiveLockAsync(
        AppDbContext db,
        long seatId,
        string token,
        DateTime now,
        long userId = 7)
    {
        db.Add(new SeatLock
        {
            SessionId = 10,
            SeatId = seatId,
            UserId = userId,
            LockToken = token,
            LockStatus = "ACTIVE",
            LockTime = now.AddMinutes(-1),
            ExpireTime = now.AddMinutes(9)
        });
        await db.SaveChangesAsync();
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
