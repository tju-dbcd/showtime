using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ShowtimeBackend.Common;
using ShowtimeBackend.Common.TicketSecurity;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Entities.SeatZone;
using ShowtimeBackend.Services.OrderTicket;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class OrderExpirationConcurrencyTests
{
    [Fact]
    public async Task PaymentLoadedBeforeExpiration_CannotIssueAfterReservationIsCancelled()
    {
        var connectionString =
            $"Data Source=order-expiration-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;" +
            "Pooling=False;Foreign Keys=False";
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        await using (var setup = CreateDbContext(keeper))
        {
            await setup.Database.EnsureCreatedAsync();
            var order = CreateOrder();
            setup.Add(order);
            setup.Add(new SeatReservation
            {
                SeatReservationId = 1,
                SessionId = 10,
                SeatId = 100,
                OrderItemId = 1,
                ReservationType = "ORDER",
                ReservationStatus = "ACTIVE",
                ReserveTime = new DateTime(2026, 9, 3, 11, 50, 0),
            });
            await setup.SaveChangesAsync();
        }

        await using var paymentConnection = new SqliteConnection(connectionString);
        await using var expirationConnection = new SqliteConnection(connectionString);
        await paymentConnection.OpenAsync();
        await expirationConnection.OpenAsync();
        await using var paymentDb = CreateDbContext(paymentConnection);
        await using var expirationDb = CreateDbContext(expirationConnection);
        var expirationService = new OrderExpirationService(
            expirationDb,
            new FixedTimeProvider(new DateTimeOffset(2026, 9, 3, 12, 2, 0, TimeSpan.Zero)),
            Options.Create(new OrderExpirationOptions()),
            NullLogger<OrderExpirationService>.Instance);
        var tokenService = new ExpireBeforeReturningTokenService(expirationService);
        var paymentService = new PaymentService(
            paymentDb,
            new FixedTimeProvider(new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero)),
            new TicketIssuanceService(tokenService),
            NullLogger<PaymentService>.Instance,
            new NullOrderTicketAuditSink(),
            new OrderExpirationService(
                paymentDb,
                TimeProvider.System,
                Options.Create(new OrderExpirationOptions()),
                NullLogger<OrderExpirationService>.Instance));

        var result = await paymentService.PayAsync(
            7,
            "alice",
            1,
            new MockPaymentRequest(PaymentChannel.ALIPAY, PaymentResult.SUCCESS),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        await using var verificationConnection = new SqliteConnection(connectionString);
        await verificationConnection.OpenAsync();
        await using var verification = CreateDbContext(verificationConnection);
        Assert.Equal("CANCELLED", (await verification.Set<Order>().SingleAsync()).OrderStatus);
        Assert.Equal(
            "CANCELLED",
            (await verification.SeatReservations.SingleAsync()).ReservationStatus);
        Assert.Empty(await verification.Set<ETicket>().ToListAsync());
        Assert.Empty(await verification.Set<Payment>().Where(item => item.PayStatus == "SUCCESS").ToListAsync());
    }

    [Fact]
    public async Task PaymentWinsBeforeExpiration_ExpirationSkipsAndCompetingRequestReportsCurrentState()
    {
        var connectionString =
            $"Data Source=payment-wins-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;" +
            "Pooling=False;Foreign Keys=False";
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        await using (var setup = CreateDbContext(keeper))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Add(CreateOrder());
            setup.Add(new SeatReservation
            {
                SeatReservationId = 1,
                SessionId = 10,
                SeatId = 100,
                OrderItemId = 1,
                ReservationType = "ORDER",
                ReservationStatus = "ACTIVE",
                ReserveTime = new DateTime(2026, 9, 3, 11, 50, 0),
            });
            await setup.SaveChangesAsync();
        }

        await using var competingConnection = new SqliteConnection(connectionString);
        await using var winnerConnection = new SqliteConnection(connectionString);
        await using var expirationConnection = new SqliteConnection(connectionString);
        await competingConnection.OpenAsync();
        await winnerConnection.OpenAsync();
        await expirationConnection.OpenAsync();
        await using var competingDb = CreateDbContext(competingConnection);
        await using var winnerDb = CreateDbContext(winnerConnection);
        await using var expirationDb = CreateDbContext(expirationConnection);
        var winnerPaymentService = new PaymentService(
            winnerDb,
            new FixedTimeProvider(new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero)),
            new TicketIssuanceService(new FixedTicketTokenService()),
            NullLogger<PaymentService>.Instance,
            new NullOrderTicketAuditSink(),
            new OrderExpirationService(
                winnerDb,
                TimeProvider.System,
                Options.Create(new OrderExpirationOptions()),
                NullLogger<OrderExpirationService>.Instance));
        var realExpirationService = new OrderExpirationService(
            expirationDb,
            new FixedTimeProvider(new DateTimeOffset(2026, 9, 3, 12, 2, 0, TimeSpan.Zero)),
            Options.Create(new OrderExpirationOptions()),
            NullLogger<OrderExpirationService>.Instance);
        var racingExpirationService = new PayThenExpireService(
            winnerPaymentService,
            realExpirationService);
        var competingPaymentService = new PaymentService(
            competingDb,
            new FixedTimeProvider(new DateTimeOffset(2026, 9, 3, 12, 2, 0, TimeSpan.Zero)),
            new TicketIssuanceService(new FixedTicketTokenService()),
            NullLogger<PaymentService>.Instance,
            new NullOrderTicketAuditSink(),
            racingExpirationService);

        var result = await competingPaymentService.PayAsync(
            7,
            "competing-user",
            1,
            new MockPaymentRequest(PaymentChannel.WECHAT, PaymentResult.SUCCESS),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("PAYMENT_ALREADY_SUCCEEDED", result.ErrorCode);
        Assert.Equal(OrderExpirationOutcome.Skipped, racingExpirationService.Outcome);
        await using var verificationConnection = new SqliteConnection(connectionString);
        await verificationConnection.OpenAsync();
        await using var verification = CreateDbContext(verificationConnection);
        Assert.Equal("ISSUED", (await verification.Set<Order>().SingleAsync()).OrderStatus);
        Assert.Single(await verification.Set<ETicket>().ToListAsync());
        Assert.Single(await verification.Set<Payment>()
            .Where(item => item.PayStatus == "SUCCESS")
            .ToListAsync());
        Assert.Equal(
            "ACTIVE",
            (await verification.SeatReservations.SingleAsync()).ReservationStatus);
    }

    private static AppDbContext CreateDbContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<SqliteAuthDbContext>()
            .UseSqlite(connection)
            .Options;
        return new SqliteAuthDbContext(options);
    }

    private static Order CreateOrder()
    {
        var order = new Order
        {
            OrderId = 1,
            OrderNo = "ORD000001",
            UserId = 7,
            SessionId = 10,
            OrderType = "NORMAL",
            TotalAmount = 100m,
            TicketCount = 1,
            OrderStatus = "PENDING_PAY",
            ExpireTime = new DateTime(2026, 9, 3, 12, 1, 0),
            Source = "WEB",
        };
        order.Items.Add(new OrderItem
        {
            OrderItemId = 1,
            OrderId = 1,
            SeatId = 100,
            PriceStrategyId = 200,
            UnitPrice = 100m,
            ItemStatus = "NORMAL",
            Order = order,
        });
        return order;
    }

    private sealed class ExpireBeforeReturningTokenService(
        IOrderExpirationService expirationService) : ITicketTokenService
    {
        public TicketCredential Generate(DateTimeOffset issuedAt)
        {
            var outcome = expirationService.ExpireOrderAsync(
                    1,
                    OrderExpirationService.SystemActor,
                    new DateTime(2026, 9, 3, 12, 2, 0, DateTimeKind.Utc))
                .GetAwaiter()
                .GetResult();
            Assert.Equal(OrderExpirationOutcome.Expired, outcome);
            return new TicketCredential("TKT-RACE", "anti-race", "qr-race");
        }

        public bool TryValidate(string qrCode, out TicketTokenPayload? payload)
        {
            payload = null;
            return false;
        }
    }

    private sealed class PayThenExpireService(
        IPaymentService winnerPaymentService,
        IOrderExpirationService expirationService) : IOrderExpirationService
    {
        public OrderExpirationOutcome? Outcome { get; private set; }

        public Task<OrderExpirationBatchResult> ExpireDueBatchAsync(
            long? afterOrderId = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async Task<OrderExpirationOutcome> ExpireOrderAsync(
            long orderId,
            string actor,
            DateTime now,
            CancellationToken cancellationToken = default)
        {
            var payment = await winnerPaymentService.PayAsync(
                7,
                "winner",
                orderId,
                new MockPaymentRequest(PaymentChannel.ALIPAY, PaymentResult.SUCCESS),
                cancellationToken);
            if (!payment.IsSuccess)
                throw new InvalidOperationException(payment.ErrorCode);

            Outcome = await expirationService.ExpireOrderAsync(
                orderId,
                actor,
                now,
                cancellationToken);
            return Outcome.Value;
        }
    }

    private sealed class FixedTicketTokenService : ITicketTokenService
    {
        public TicketCredential Generate(DateTimeOffset issuedAt) =>
            new("TKT-WINNER", "anti-winner", "qr-winner");

        public bool TryValidate(string qrCode, out TicketTokenPayload? payload)
        {
            payload = null;
            return false;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
