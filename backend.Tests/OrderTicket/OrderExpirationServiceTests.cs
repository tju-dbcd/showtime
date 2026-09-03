using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ShowtimeBackend.Data;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Entities.SeatZone;
using ShowtimeBackend.Services.OrderTicket;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class OrderExpirationServiceTests
{
    private static readonly DateTime Now = new(2026, 9, 3, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ExpireDueBatchAsync_OnlyExpiresDuePendingNonExchangeOrders()
    {
        await using var connection = await CreateConnectionAsync();
        await using var db = await CreateDbContextAsync(connection);
        db.AddRange(
            CreateOrder(1, "PENDING_PAY", "NORMAL", Now.AddSeconds(-1)),
            CreateOrder(2, "PENDING_PAY", "NORMAL", Now.AddSeconds(1)),
            CreateOrder(3, "PAID", "NORMAL", Now.AddSeconds(-1)),
            CreateOrder(4, "PENDING_PAY", "EXCHANGE", Now.AddSeconds(-1)));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.ExpireDueBatchAsync(cancellationToken: CancellationToken.None);

        Assert.Equal(1, result.CandidateCount);
        Assert.Equal(1, result.ExpiredCount);
        var orders = await db.Set<Order>().AsNoTracking().OrderBy(item => item.OrderId).ToListAsync();
        Assert.Equal("CANCELLED", orders[0].OrderStatus);
        Assert.Equal("PENDING_PAY", orders[1].OrderStatus);
        Assert.Equal("PAID", orders[2].OrderStatus);
        Assert.Equal("PENDING_PAY", orders[3].OrderStatus);
    }

    [Fact]
    public async Task ExpireOrderAsync_AtomicallyCancelsActiveOrderReservationsAndPendingPayments()
    {
        await using var connection = await CreateConnectionAsync();
        await using var db = await CreateDbContextAsync(connection);
        var order = CreateOrder(1, "PENDING_PAY", "NORMAL", Now);
        order.TicketCount = 2;
        order.Items.Add(new OrderItem
        {
            OrderItemId = 102,
            OrderId = order.OrderId,
            SeatId = 102,
            PriceStrategyId = 200,
            UnitPrice = 100m,
            ItemStatus = "NORMAL",
            Order = order,
        });
        order.Payments.Add(CreatePayment(1, order, "PENDING"));
        order.Payments.Add(CreatePayment(2, order, "SUCCESS"));
        db.Add(order);
        db.AddRange(
            CreateReservation(1, 101, "ORDER", "ACTIVE"),
            CreateReservation(2, 102, "ORDER", "RELEASED"),
            CreateReservation(3, null, "SYSTEM", "ACTIVE"),
            new SeatLock
            {
                SeatLockId = 1,
                SessionId = 10,
                SeatId = 100,
                UserId = 7,
                LockToken = "converted-lock",
                LockStatus = "CONVERTED",
                LockTime = Now.AddMinutes(-20),
                ExpireTime = Now.AddMinutes(-10),
                UpdateBy = "creator",
            });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var outcome = await service.ExpireOrderAsync(
            1,
            OrderExpirationService.SystemActor,
            Now,
            CancellationToken.None);

        Assert.Equal(OrderExpirationOutcome.Expired, outcome);
        var savedOrder = await db.Set<Order>().AsNoTracking().SingleAsync();
        Assert.Equal("CANCELLED", savedOrder.OrderStatus);
        Assert.Equal(Now, savedOrder.CancelTime);
        Assert.Equal(OrderExpirationService.SystemActor, savedOrder.UpdateBy);
        var reservations = await db.SeatReservations.AsNoTracking()
            .OrderBy(item => item.SeatReservationId).ToListAsync();
        Assert.Equal("CANCELLED", reservations[0].ReservationStatus);
        Assert.Equal(Now, reservations[0].CancelTime);
        Assert.Equal(OrderExpirationService.SystemActor, reservations[0].UpdateBy);
        Assert.Equal("RELEASED", reservations[1].ReservationStatus);
        Assert.Null(reservations[1].CancelTime);
        Assert.Equal("ACTIVE", reservations[2].ReservationStatus);
        var payments = await db.Set<Payment>().AsNoTracking()
            .OrderBy(item => item.PaymentId).ToListAsync();
        Assert.Equal("CLOSED", payments[0].PayStatus);
        Assert.Equal(OrderExpirationService.SystemActor, payments[0].UpdateBy);
        Assert.Equal("SUCCESS", payments[1].PayStatus);
        var seatLock = await db.SeatLocks.AsNoTracking().SingleAsync();
        Assert.Equal("CONVERTED", seatLock.LockStatus);
        Assert.Equal("creator", seatLock.UpdateBy);
    }

    [Fact]
    public async Task ExpireOrderAsync_RepeatedExecutionIsIdempotent()
    {
        await using var connection = await CreateConnectionAsync();
        await using var db = await CreateDbContextAsync(connection);
        db.Add(CreateOrder(1, "PENDING_PAY", "NORMAL", Now));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var first = await service.ExpireOrderAsync(1, "order-expiration", Now);
        var second = await service.ExpireOrderAsync(1, "other-actor", Now.AddMinutes(1));

        Assert.Equal(OrderExpirationOutcome.Expired, first);
        Assert.Equal(OrderExpirationOutcome.Skipped, second);
        var order = await db.Set<Order>().AsNoTracking().SingleAsync();
        Assert.Equal(Now, order.CancelTime);
        Assert.Equal("order-expiration", order.UpdateBy);
    }

    [Fact]
    public async Task ExpireDueBatchAsync_PoisonOrderDoesNotBlockLaterCandidateAndCursorAdvances()
    {
        await using var connection = await CreateConnectionAsync();
        await using var db = await CreateDbContextAsync(connection);
        db.AddRange(
            CreateOrder(1, "PENDING_PAY", "NORMAL", Now),
            CreateOrder(2, "PENDING_PAY", "NORMAL", Now),
            CreateOrder(3, "PENDING_PAY", "NORMAL", Now));
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TRIGGER poison_order
            BEFORE UPDATE OF ORDER_STATUS ON T_ORDER
            WHEN OLD.ORDER_ID = 1
            BEGIN
                SELECT RAISE(ABORT, 'poison order');
            END;
            """);
        var service = CreateService(db, batchSize: 2);

        var first = await service.ExpireDueBatchAsync();
        var second = await service.ExpireDueBatchAsync(first.LastOrderId);

        Assert.Equal(2, first.CandidateCount);
        Assert.Equal(1, first.ExpiredCount);
        Assert.Equal(1, first.FailureCount);
        Assert.Equal(2, first.LastOrderId);
        Assert.Equal(1, second.CandidateCount);
        Assert.Equal(1, second.ExpiredCount);
        Assert.Equal(3, second.LastOrderId);
        var statuses = await db.Set<Order>().AsNoTracking()
            .OrderBy(item => item.OrderId)
            .Select(item => item.OrderStatus)
            .ToListAsync();
        Assert.Equal(["PENDING_PAY", "CANCELLED", "CANCELLED"], statuses);
    }

    [Fact]
    public void ServiceDependencyBoundary_DoesNotIncludeSeatLockGuard()
    {
        var dependencyTypes = typeof(OrderExpirationService).GetConstructors()
            .Single().GetParameters().Select(parameter => parameter.ParameterType);

        Assert.DoesNotContain(typeof(ShowtimeBackend.Services.SeatZone.ISeatLockGuard), dependencyTypes);
    }

    private static OrderExpirationService CreateService(AppDbContext db, int batchSize = 50) => new(
        db,
        new FixedTimeProvider(new DateTimeOffset(Now)),
        Options.Create(new OrderExpirationOptions { ExpirationBatchSize = batchSize }),
        NullLogger<OrderExpirationService>.Instance);

    private static async Task<SqliteConnection> CreateConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<AppDbContext> CreateDbContextAsync(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<SqliteAuthDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new SqliteAuthDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
        return db;
    }

    private static Order CreateOrder(
        long orderId,
        string status,
        string type,
        DateTime expireTime)
    {
        var order = new Order
        {
            OrderId = orderId,
            OrderNo = $"ORD{orderId:000000}",
            UserId = 7,
            SessionId = 10,
            OrderType = type,
            TotalAmount = 100m,
            TicketCount = 1,
            OrderStatus = status,
            ExpireTime = expireTime,
            Source = "WEB",
        };
        order.Items.Add(new OrderItem
        {
            OrderItemId = 100 + orderId,
            OrderId = orderId,
            SeatId = 100 + orderId,
            PriceStrategyId = 200,
            UnitPrice = 100m,
            ItemStatus = "NORMAL",
            Order = order,
        });
        return order;
    }

    private static Payment CreatePayment(long id, Order order, string status) => new()
    {
        PaymentId = id,
        PaymentNo = $"PAY{id:000000}",
        OrderId = order.OrderId,
        UserId = order.UserId,
        PayAmount = order.TotalAmount,
        PayChannel = "ALIPAY",
        PayStatus = status,
        Order = order,
    };

    private static SeatReservation CreateReservation(
        long id,
        long? orderItemId,
        string type,
        string status) => new()
    {
        SeatReservationId = id,
        SessionId = 10,
        SeatId = 100 + id,
        OrderItemId = orderItemId,
        ReservationType = type,
        ReservationStatus = status,
        ReserveTime = Now.AddMinutes(-10),
    };

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
