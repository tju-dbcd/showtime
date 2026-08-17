using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Common;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Services.OrderTicket;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class PaymentServiceTests
{
    [Fact]
    public async Task PayAsync_SuccessCreatesPaymentAndMarksOrderPaid()
    {
        await using var db = CreateDbContext();
        db.Add(CreateOrder(expireTime: new DateTime(2026, 8, 2, 12, 30, 0)));
        await db.SaveChangesAsync();
        var now = new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
        var service = new PaymentService(db, new FixedTimeProvider(now));

        var result = await service.PayAsync(
            7,
            "alice",
            1,
            new MockPaymentRequest(PaymentChannel.ALIPAY, PaymentResult.SUCCESS),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(150m, result.Value!.PayAmount);
        Assert.Equal(PaymentStatus.SUCCESS, result.Value.PayStatus);
        Assert.Equal(now.UtcDateTime, result.Value.PayTime);
        Assert.Equal("PAID", (await db.Set<Order>().SingleAsync()).OrderStatus);
    }

    [Fact]
    public async Task PayAsync_FailureKeepsOrderPendingPayment()
    {
        await using var db = CreateDbContext();
        db.Add(CreateOrder(expireTime: new DateTime(2026, 8, 2, 12, 30, 0)));
        await db.SaveChangesAsync();
        var service = new PaymentService(
            db,
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero)));

        var result = await service.PayAsync(
            7,
            "alice",
            1,
            new MockPaymentRequest(PaymentChannel.WECHAT, PaymentResult.FAIL),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.FAIL, result.Value!.PayStatus);
        Assert.Null(result.Value.PayTime);
        Assert.Equal("PENDING_PAY", (await db.Set<Order>().SingleAsync()).OrderStatus);
    }

    [Fact]
    public async Task PayAsync_RejectsSecondSuccessfulPayment()
    {
        await using var db = CreateDbContext();
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
        var service = new PaymentService(
            db,
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero)));

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
        await using var db = CreateDbContext();
        db.Add(CreateOrder(expireTime: new DateTime(2026, 8, 2, 11, 59, 59)));
        await db.SaveChangesAsync();
        var service = new PaymentService(
            db,
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero)));

        var result = await service.PayAsync(
            7,
            "alice",
            1,
            new MockPaymentRequest(PaymentChannel.ALIPAY, PaymentResult.SUCCESS),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("ORDER_EXPIRED", result.ErrorCode);
        Assert.Equal("CANCELLED", (await db.Set<Order>().SingleAsync()).OrderStatus);
        Assert.Empty(await db.Set<Payment>().ToListAsync());
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static Order CreateOrder(DateTime expireTime) => new()
    {
        OrderId = 1,
        OrderNo = "ORD000001",
        UserId = 7,
        SessionId = 10,
        TotalAmount = 200m,
        DiscountAmount = 50m,
        TicketCount = 1,
        OrderStatus = "PENDING_PAY",
        ExpireTime = expireTime,
        Source = "WEB"
    };

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
