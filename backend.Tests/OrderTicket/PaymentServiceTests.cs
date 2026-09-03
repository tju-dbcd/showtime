using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using ShowtimeBackend.Common;
using ShowtimeBackend.Common.TicketSecurity;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Entities.SeatZone;
using ShowtimeBackend.Services.OrderTicket;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class PaymentServiceTests
{
    [Fact]
    public async Task PayAsync_SuccessCreatesPaymentAndMarksOrderIssued()
    {
        await using var connection = await CreateConnectionAsync();
        await using var db = await CreateDbContextAsync(connection);
        db.Add(CreateOrder(expireTime: new DateTime(2026, 8, 2, 12, 30, 0)));
        await db.SaveChangesAsync();
        var now = new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
        var service = CreateService(db, now);

        var result = await service.PayAsync(
            7,
            "alice",
            1,
            new MockPaymentRequest(PaymentChannel.ALIPAY, PaymentResult.SUCCESS),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(150m, result.Value!.Payment.PayAmount);
        Assert.Equal(PaymentStatus.SUCCESS, result.Value.Payment.PayStatus);
        Assert.Equal(now.UtcDateTime, result.Value.Payment.PayTime);
        Assert.Equal(OrderStatus.ISSUED, result.Value.OrderStatus);
        Assert.Equal(1, result.Value.IssuedTicketCount);
        var savedOrder = await db.Set<Order>().SingleAsync();
        Assert.Equal("ISSUED", savedOrder.OrderStatus);
        Assert.Equal(now.UtcDateTime, savedOrder.IssueTime);
        var ticket = await db.Set<ETicket>().SingleAsync();
        Assert.Equal("UNUSED", ticket.TicketStatus);
        Assert.Equal(1, ticket.OrderItemId);
    }

    [Fact]
    public async Task PayAsync_FailureKeepsOrderPendingPayment()
    {
        await using var connection = await CreateConnectionAsync();
        await using var db = await CreateDbContextAsync(connection);
        db.Add(CreateOrder(expireTime: new DateTime(2026, 8, 2, 12, 30, 0)));
        await db.SaveChangesAsync();
        var service = CreateService(
            db,
            new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero));

        var result = await service.PayAsync(
            7,
            "alice",
            1,
            new MockPaymentRequest(PaymentChannel.WECHAT, PaymentResult.FAIL),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.FAIL, result.Value!.Payment.PayStatus);
        Assert.Null(result.Value.Payment.PayTime);
        Assert.Equal(OrderStatus.PENDING_PAY, result.Value.OrderStatus);
        Assert.Equal(0, result.Value.IssuedTicketCount);
        Assert.Equal("PENDING_PAY", (await db.Set<Order>().SingleAsync()).OrderStatus);
        Assert.Empty(await db.Set<ETicket>().ToListAsync());
    }

    [Fact]
    public async Task PayAsync_RejectsSecondSuccessfulPayment()
    {
        await using var connection = await CreateConnectionAsync();
        await using var db = await CreateDbContextAsync(connection);
        var order = CreateOrder(expireTime: new DateTime(2026, 8, 2, 12, 30, 0));
        order.Payments.Add(new Payment
        {
            PaymentId = 2,
            PaymentNo = "PAY000002",
            UserId = 7,
            PayAmount = 150m,
            PayChannel = "ALIPAY",
            PayStatus = "SUCCESS"
        });
        db.Add(order);
        await db.SaveChangesAsync();
        var service = CreateService(
            db,
            new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero));

        var result = await service.PayAsync(
            7,
            "alice",
            1,
            new MockPaymentRequest(PaymentChannel.ALIPAY, PaymentResult.SUCCESS),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderTicketFailure.Conflict, result.Failure);
        Assert.Equal("PAYMENT_ALREADY_SUCCEEDED", result.ErrorCode);
        Assert.Equal(1, await db.Set<Payment>().CountAsync());
    }

    [Fact]
    public async Task PayAsync_CancelsExpiredOrder()
    {
        await using var connection = await CreateConnectionAsync();
        await using var db = await CreateDbContextAsync(connection);
        var order = CreateOrder(expireTime: new DateTime(2026, 8, 2, 11, 59, 59));
        order.Payments.Add(new Payment
        {
            PaymentId = 2,
            PaymentNo = "PAY000002",
            UserId = 7,
            PayAmount = 150m,
            PayChannel = "ALIPAY",
            PayStatus = "PENDING",
        });
        db.Add(order);
        db.Add(new SeatReservation
        {
            SeatReservationId = 1,
            SessionId = 10,
            SeatId = 100,
            OrderItemId = 1,
            ReservationType = "ORDER",
            ReservationStatus = "ACTIVE",
            ReserveTime = new DateTime(2026, 8, 2, 11, 45, 0),
        });
        await db.SaveChangesAsync();
        var service = CreateService(
            db,
            new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero));

        var result = await service.PayAsync(
            7,
            "alice",
            1,
            new MockPaymentRequest(PaymentChannel.ALIPAY, PaymentResult.SUCCESS),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("ORDER_EXPIRED", result.ErrorCode);
        Assert.Equal("CANCELLED", (await db.Set<Order>().SingleAsync()).OrderStatus);
        Assert.Equal("CANCELLED", (await db.SeatReservations.SingleAsync()).ReservationStatus);
        Assert.Equal("CLOSED", (await db.Set<Payment>().SingleAsync()).PayStatus);
    }

    [Fact]
    public async Task PayAsync_WhenSecondTicketGenerationFails_PersistsNothing()
    {
        await using var connection = await CreateConnectionAsync();
        await using var db = await CreateDbContextAsync(connection);
        db.Add(CreateOrder(
            expireTime: new DateTime(2026, 8, 2, 12, 30, 0),
            itemCount: 2));
        await db.SaveChangesAsync();
        var now = new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
        var service = new PaymentService(
            db,
            new FixedTimeProvider(now),
            new TicketIssuanceService(
                new ThrowOnSecondGenerateTokenService(CreateTokenService())),
            NullLogger<PaymentService>.Instance,
            new NullOrderTicketAuditSink(),
            CreateExpirationService(db, now));

        var result = await service.PayAsync(
            7,
            "alice",
            1,
            new MockPaymentRequest(PaymentChannel.ALIPAY, PaymentResult.SUCCESS),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("TICKET_ISSUANCE_FAILED", result.ErrorCode);
        await using var verificationDb = await CreateDbContextAsync(connection);
        var savedOrder = await verificationDb.Set<Order>().AsNoTracking().SingleAsync();
        Assert.Equal("PENDING_PAY", savedOrder.OrderStatus);
        Assert.Null(savedOrder.PayTime);
        Assert.Null(savedOrder.IssueTime);
        Assert.Empty(await verificationDb.Set<Payment>().AsNoTracking().ToListAsync());
        Assert.Empty(await verificationDb.Set<ETicket>().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task PayAsync_WhenAuditSinkFails_KeepsCommittedIssuanceSuccessful()
    {
        await using var connection = await CreateConnectionAsync();
        await using var db = await CreateDbContextAsync(connection);
        db.Add(CreateOrder(expireTime: new DateTime(2026, 8, 2, 12, 30, 0)));
        await db.SaveChangesAsync();
        var service = CreateService(
            db,
            new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero),
            new ThrowingAuditSink());

        var result = await service.PayAsync(
            7,
            "alice",
            1,
            new MockPaymentRequest(PaymentChannel.ALIPAY, PaymentResult.SUCCESS),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("ISSUED", (await db.Set<Order>().SingleAsync()).OrderStatus);
        Assert.Single(await db.Set<ETicket>().ToListAsync());
    }

    private static async Task<SqliteConnection> CreateConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<AppDbContext> CreateDbContextAsync(
        SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<SqliteAuthDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new SqliteAuthDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
        return db;
    }

    private static PaymentService CreateService(
        AppDbContext db,
        DateTimeOffset now,
        IOrderTicketAuditSink? auditSink = null) => new(
            db,
            new FixedTimeProvider(now),
            new TicketIssuanceService(CreateTokenService()),
            NullLogger<PaymentService>.Instance,
            auditSink ?? new NullOrderTicketAuditSink(),
            CreateExpirationService(db, now));

    private static OrderExpirationService CreateExpirationService(
        AppDbContext db,
        DateTimeOffset now) => new(
        db,
        new FixedTimeProvider(now),
        Options.Create(new OrderExpirationOptions()),
        NullLogger<OrderExpirationService>.Instance);

    private static HmacTicketTokenService CreateTokenService() => new(
        Options.Create(new TicketSecurityOptions
        {
            SigningKeyBase64 =
                "ERERERERERERERERERERERERERERERERERERERERERE=",
        }));

    private static Order CreateOrder(DateTime expireTime, int itemCount = 1)
    {
        var order = new Order
        {
            OrderId = 1,
            OrderNo = "ORD000001",
            UserId = 7,
            SessionId = 10,
            TotalAmount = itemCount * 200m,
            DiscountAmount = itemCount * 50m,
            TicketCount = itemCount,
            OrderStatus = "PENDING_PAY",
            ExpireTime = expireTime,
            Source = "WEB",
        };
        for (var index = 0; index < itemCount; index++)
        {
            order.Items.Add(new OrderItem
            {
                OrderItemId = index + 1,
                OrderId = order.OrderId,
                SeatId = 100 + index,
                PriceStrategyId = 200,
                UnitPrice = 150m,
                ItemStatus = "NORMAL",
                Order = order,
            });
        }
        return order;
    }

    private sealed class ThrowOnSecondGenerateTokenService(
        ITicketTokenService inner) : ITicketTokenService
    {
        private int _generateCount;

        public TicketCredential Generate(DateTimeOffset issuedAt)
        {
            _generateCount++;
            return _generateCount == 2
                ? throw new InvalidOperationException("Simulated token generation failure.")
                : inner.Generate(issuedAt);
        }

        public bool TryValidate(
            string qrCode,
            out TicketTokenPayload? payload) =>
            inner.TryValidate(qrCode, out payload);
    }

    private sealed class ThrowingAuditSink : IOrderTicketAuditSink
    {
        public ValueTask WriteAsync(
            OrderTicketAuditEvent auditEvent,
            CancellationToken cancellationToken) =>
            ValueTask.FromException(
                new InvalidOperationException("Simulated audit failure."));
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
