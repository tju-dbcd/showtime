using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Data.Sqlite;
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

public sealed class ExchangeConcurrencyTests
{
    [Fact]
    public async Task TwoIndependentContexts_CreateSameExchange_ExactlyOneSucceeds()
    {
        await using var source = await ExchangeQuoteServiceTests.CreateFixtureAsync(
            [105m], [125m], fee: 5m);
        var databaseName = $"exchange-concurrency-{Guid.NewGuid():N}";
        var connectionString =
            $"Data Source={databaseName};Mode=Memory;Cache=Shared;Default Timeout=30;" +
            "Pooling=False;Foreign Keys=False";
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        source.BackupTo(keeper);
        await using var firstDb = CreateSharedContext(connectionString);
        await using var secondDb = CreateSharedContext(connectionString);
        var first = new ExchangeApplicationService(
            firstDb,
            new ExchangePolicyEngine(),
            source.TimeProvider,
            new OracleExchangeLockCoordinator(firstDb));
        var second = new ExchangeApplicationService(
            secondDb,
            new ExchangePolicyEngine(),
            source.TimeProvider,
            new OracleExchangeLockCoordinator(secondDb));
        var request = new CreateExchangeRequest(
            22,
            [new ExchangeTargetItemRequest(101, 701, 801, "lock-701")],
            null);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var start = new TwoPartyAsyncBarrier();
        var results = await Task.WhenAll(
            RunCreateAsync(first, "alice-1"),
            RunCreateAsync(second, "alice-2"));

        Assert.Single(results, result => result.IsSuccess);
        var loser = Assert.Single(results, result => !result.IsSuccess);
        Assert.Contains(
            loser.ErrorCode,
            new[] { "EXCHANGE_ACTIVE_REQUEST_EXISTS", "EXCHANGE_ITEM_NOT_ELIGIBLE" });
        await using var verification = CreateSharedContext(connectionString);
        Assert.Equal(1, await verification.Set<ExchangeRequest>().AsNoTracking().CountAsync());
        Assert.Equal(1, await verification.Set<ExchangeItem>().AsNoTracking().CountAsync());
        Assert.Equal("EXCHANGING", await verification.Set<ETicket>().AsNoTracking()
            .Where(item => item.OrderItemId == 101)
            .Select(item => item.TicketStatus)
            .SingleAsync());

        async Task<OrderTicketResult<ExchangeResponse>> RunCreateAsync(
            ExchangeApplicationService service,
            string actor)
        {
            await start.SignalAndWaitAsync(timeout.Token);
            return await service.CreateAsync(7, actor, 11, request, timeout.Token);
        }
    }

    [Fact]
    public async Task TwoIndependentContexts_RedeemVersusExchange_OnlyOneOwnsOriginalTicket()
    {
        await using var source = await ExchangeQuoteServiceTests.CreateFixtureAsync(
            [105m], [125m], fee: 5m);
        var session = await source.Db.Set<ShowtimeBackend.Entities.ShowSession.ShowSession>()
            .SingleAsync(item => item.SessionId == 21);
        session.StartTime = RefundTestData.FixedUtcNow.AddMinutes(-30);
        session.EndTime = RefundTestData.FixedUtcNow.AddMinutes(30);
        var policy = await source.Db.Set<ExchangePolicy>().SingleAsync();
        policy.ExchangeDeadlineHour = 0;
        await source.Db.SaveChangesAsync();
        await using var shared = await SharedExchangeDatabase.CreateAsync(source);
        await using var exchangeDb = shared.CreateContext();
        await using var redemptionDb = shared.CreateContext();
        var exchange = CreateApplication(exchangeDb, source.TimeProvider);
        var redemption = new TicketRedemptionService(
            redemptionDb,
            new ExistingTicketTokenService("TKT000201", "qr-201"),
            source.TimeProvider,
            Options.Create(new TicketRedemptionOptions()),
            NullLogger<TicketRedemptionService>.Instance,
            new NullOrderTicketAuditSink());
        var barrier = new TwoPartyAsyncBarrier();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var results = await Task.WhenAll(
            RunExchangeAsync(),
            RunRedeemAsync());

        Assert.Single(results, item => item);
        await using var verification = shared.CreateContext();
        var ticket = await verification.Set<ETicket>().AsNoTracking().SingleAsync(
            item => item.OrderItemId == 101);
        Assert.Contains(ticket.TicketStatus, new[] { "USED", "EXCHANGING" });
        Assert.Equal(ticket.TicketStatus == "EXCHANGING" ? 1 : 0,
            await verification.Set<ExchangeRequest>().AsNoTracking().CountAsync());

        async Task<bool> RunExchangeAsync()
        {
            await barrier.SignalAndWaitAsync(timeout.Token);
            var result = await exchange.CreateAsync(
                7, "exchange-racer", 11,
                new CreateExchangeRequest(
                    22, [new(101, 701, 801, "lock-701")], null),
                timeout.Token);
            return result.IsSuccess;
        }

        async Task<bool> RunRedeemAsync()
        {
            await barrier.SignalAndWaitAsync(timeout.Token);
            var result = await redemption.RedeemAsync(
                "gate-racer",
                new RedeemTicketRequest("qr-201", "gate-1"),
                timeout.Token);
            return result.IsSuccess;
        }
    }

    [Fact]
    public async Task TwoIndependentContexts_RefundVersusExchange_OnlyOneFreezesOriginalTicket()
    {
        await using var source = await ExchangeQuoteServiceTests.CreateFixtureAsync(
            [105m], [125m], fee: 5m);
        source.Db.Add(new RefundPolicy
        {
            PolicyId = 901,
            ShowId = 90,
            PolicyName = "concurrency refund",
            RefundDeadlineHour = 0,
            RefundRate = 1m,
            ServiceFee = 0m,
            Priority = 1,
            Status = 1,
        });
        await source.Db.SaveChangesAsync();
        await using var shared = await SharedExchangeDatabase.CreateAsync(source);
        await using var exchangeDb = shared.CreateContext();
        await using var refundDb = shared.CreateContext();
        var exchange = CreateApplication(exchangeDb, source.TimeProvider);
        var refund = new RefundApplicationService(
            refundDb,
            new RefundPolicyEngine(),
            source.TimeProvider,
            new OracleRefundLockCoordinator(refundDb),
            NullLogger<RefundApplicationService>.Instance,
            new NullOrderTicketAuditSink());
        var barrier = new TwoPartyAsyncBarrier();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var results = await Task.WhenAll(RunExchangeAsync(), RunRefundAsync());

        Assert.Single(results, item => item);
        await using var verification = shared.CreateContext();
        var ticket = await verification.Set<ETicket>().AsNoTracking()
            .SingleAsync(item => item.OrderItemId == 101);
        Assert.Contains(ticket.TicketStatus, new[] { "REFUNDING", "EXCHANGING" });
        Assert.Equal(1,
            await verification.Set<RefundRequest>().CountAsync() +
            await verification.Set<ExchangeRequest>().CountAsync());

        async Task<bool> RunExchangeAsync()
        {
            await barrier.SignalAndWaitAsync(timeout.Token);
            return (await exchange.CreateAsync(
                7, "exchange-racer", 11,
                new CreateExchangeRequest(
                    22, [new(101, 701, 801, "lock-701")], null),
                timeout.Token)).IsSuccess;
        }

        async Task<bool> RunRefundAsync()
        {
            await barrier.SignalAndWaitAsync(timeout.Token);
            return (await refund.CreateAsync(
                7, "refund-racer", 11,
                new CreateRefundRequest([101], "concurrent refund"),
                timeout.Token)).IsSuccess;
        }
    }

    [Fact]
    public async Task TwoIndependentContexts_NormalOrderVersusExchange_OnlyOneConvertsTargetLock()
    {
        await using var source = await ExchangeQuoteServiceTests.CreateFixtureAsync(
            [105m], [125m], fee: 5m);
        await using var shared = await SharedExchangeDatabase.CreateAsync(source);
        await using var exchangeDb = shared.CreateContext();
        await using var orderDb = shared.CreateContext();
        var exchange = CreateApplication(exchangeDb, source.TimeProvider);
        var order = new OrderService(orderDb, source.TimeProvider);
        var barrier = new TwoPartyAsyncBarrier();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var results = await Task.WhenAll(RunExchangeAsync(), RunOrderAsync());

        Assert.Single(results, item => item);
        await using var verification = shared.CreateContext();
        Assert.Equal("CONVERTED", await verification.Set<SeatLock>().AsNoTracking()
            .Where(item => item.SeatLockId == 1001)
            .Select(item => item.LockStatus).SingleAsync());
        Assert.Equal(1, await verification.Set<SeatReservation>().AsNoTracking()
            .CountAsync(item => item.SessionId == 22 && item.SeatId == 701 &&
                                item.ReservationStatus == "ACTIVE"));

        async Task<bool> RunExchangeAsync()
        {
            await barrier.SignalAndWaitAsync(timeout.Token);
            return (await exchange.CreateAsync(
                7, "exchange-racer", 11,
                new CreateExchangeRequest(
                    22, [new(101, 701, 801, "lock-701")], null),
                timeout.Token)).IsSuccess;
        }

        async Task<bool> RunOrderAsync()
        {
            await barrier.SignalAndWaitAsync(timeout.Token);
            return (await order.CreateAsync(
                7,
                "order-racer",
                new CreateOrderRequest(
                    22, [new CreateOrderItemRequest(701, 801, null, "lock-701")], null),
                timeout.Token)).IsSuccess;
        }
    }

    [Fact]
    public async Task TwoIndependentContexts_ApproveVersusReviewExpiration_ReachesOneFailedTerminalState()
    {
        await using var source = await ExchangeQuoteServiceTests.CreateFixtureAsync(
            [105m], [125m], fee: 5m);
        var sourceApplication = CreateApplication(source.Db, source.TimeProvider);
        var created = await sourceApplication.CreateAsync(
            7, "exchange-user", 11,
            new CreateExchangeRequest(22, [new(101, 701, 801, "lock-701")], null));
        Assert.True(created.IsSuccess, created.Message);
        var child = await source.Db.Set<Order>().SingleAsync(item => item.OrderType == "EXCHANGE");
        child.ExpireTime = RefundTestData.FixedUtcNow;
        await source.Db.SaveChangesAsync();
        await using var shared = await SharedExchangeDatabase.CreateAsync(source);
        await using var approveDb = shared.CreateContext();
        await using var expireDb = shared.CreateContext();
        var approve = CreateReview(approveDb, source.TimeProvider);
        var expire = CreateReview(expireDb, source.TimeProvider);
        var barrier = new TwoPartyAsyncBarrier();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        await Task.WhenAll(RunApproveAsync(), RunExpireAsync());

        await AssertFailedTerminalAsync(shared, expectPayments: 0, expectNewTickets: 0);

        async Task RunApproveAsync()
        {
            await barrier.SignalAndWaitAsync(timeout.Token);
            await approve.ApproveAsync(
                "admin-racer", created.Value!.ExchangeId,
                new ApproveExchangeRequest(null), timeout.Token);
        }

        async Task RunExpireAsync()
        {
            await barrier.SignalAndWaitAsync(timeout.Token);
            await expire.ExpireAsync(
                created.Value!.ExchangeId, "expiration-racer", timeout.Token);
        }
    }

    [Fact]
    public async Task TwoIndependentContexts_PaymentVersusPaymentExpiration_ReachesOneFailedTerminalState()
    {
        await using var source = await ExchangeQuoteServiceTests.CreateFixtureAsync(
            [105m], [125m], fee: 5m);
        var sourceApplication = CreateApplication(source.Db, source.TimeProvider);
        var created = await sourceApplication.CreateAsync(
            7, "exchange-user", 11,
            new CreateExchangeRequest(22, [new(101, 701, 801, "lock-701")], null));
        var sourceReview = CreateReview(source.Db, source.TimeProvider);
        var approved = await sourceReview.ApproveAsync(
            "admin", created.Value!.ExchangeId, new ApproveExchangeRequest(null));
        Assert.True(approved.IsSuccess, approved.Message);
        var child = await source.Db.Set<Order>().SingleAsync(item => item.OrderType == "EXCHANGE");
        child.ExpireTime = RefundTestData.FixedUtcNow;
        await source.Db.SaveChangesAsync();
        await using var shared = await SharedExchangeDatabase.CreateAsync(source);
        await using var paymentDb = shared.CreateContext();
        await using var expireDb = shared.CreateContext();
        var payment = CreatePayment(paymentDb, source.TimeProvider);
        var expire = CreateReview(expireDb, source.TimeProvider);
        var barrier = new TwoPartyAsyncBarrier();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        await Task.WhenAll(RunPaymentAsync(), RunExpireAsync());

        await AssertFailedTerminalAsync(shared, expectPayments: 0, expectNewTickets: 0);

        async Task RunPaymentAsync()
        {
            await barrier.SignalAndWaitAsync(timeout.Token);
            await payment.PayAsync(
                7, "payment-racer", created.Value!.ExchangeId,
                new ExchangePaymentRequest(PaymentChannel.ALIPAY, PaymentResult.SUCCESS),
                timeout.Token);
        }

        async Task RunExpireAsync()
        {
            await barrier.SignalAndWaitAsync(timeout.Token);
            await expire.ExpireAsync(
                created.Value!.ExchangeId, "expiration-racer", timeout.Token);
        }
    }

    [Fact]
    public async Task CreateAsync_WhenSecondSaveChangesFails_RollsBackWholeAggregate()
    {
        await using var fixture = await ExchangeQuoteServiceTests.CreateFixtureAsync(
            [105m], [125m], fee: 5m);
        await using var db = fixture.CreateDbContext(
            new ThrowOnSaveChangesInterceptor(throwOnCall: 2));
        var service = new ExchangeApplicationService(
            db,
            new ExchangePolicyEngine(),
            fixture.TimeProvider);

        var result = await service.CreateAsync(
            7,
            "alice",
            11,
            new CreateExchangeRequest(
                22,
                [new ExchangeTargetItemRequest(101, 701, 801, "lock-701")],
                null));

        Assert.False(result.IsSuccess);
        Assert.Equal("EXCHANGE_CREATE_CONFLICT", result.ErrorCode);
        fixture.Db.ChangeTracker.Clear();
        Assert.Empty(await fixture.Db.Set<ExchangeRequest>().AsNoTracking().ToListAsync());
        Assert.Empty(await fixture.Db.Set<ExchangeItem>().AsNoTracking().ToListAsync());
        Assert.Empty(await fixture.Db.Set<Order>().AsNoTracking()
            .Where(item => item.OrderType == "EXCHANGE").ToListAsync());
        Assert.Empty(await fixture.Db.Set<SeatReservation>().AsNoTracking()
            .Where(item => item.HoldReason == "EXCHANGE").ToListAsync());
        Assert.Equal("NORMAL", await fixture.Db.Set<OrderItem>().AsNoTracking()
            .Where(item => item.OrderItemId == 101)
            .Select(item => item.ItemStatus).SingleAsync());
        Assert.Equal("UNUSED", await fixture.Db.Set<ETicket>().AsNoTracking()
            .Where(item => item.OrderItemId == 101)
            .Select(item => item.TicketStatus).SingleAsync());
        Assert.Equal("ACTIVE", await fixture.Db.Set<SeatLock>().AsNoTracking()
            .Where(item => item.SeatLockId == 1001)
            .Select(item => item.LockStatus).SingleAsync());
    }

    [Fact]
    public async Task SecondCreateAttempt_CannotFreezeOriginalTicketTwice()
    {
        await using var fixture = await ExchangeQuoteServiceTests.CreateFixtureAsync(
            [105m], [125m], fee: 5m);
        var service = new ExchangeApplicationService(
            fixture.Db,
            new ExchangePolicyEngine(),
            fixture.TimeProvider);
        var request = new CreateExchangeRequest(
            22,
            [new ExchangeTargetItemRequest(101, 701, 801, "lock-701")],
            null);

        var first = await service.CreateAsync(7, "alice", 11, request);
        var second = await service.CreateAsync(7, "alice", 11, request);

        Assert.True(first.IsSuccess);
        Assert.False(second.IsSuccess);
        Assert.Equal("EXCHANGE_ACTIVE_REQUEST_EXISTS", second.ErrorCode);
        Assert.Single(await fixture.Db.Set<ExchangeRequest>().AsNoTracking().ToListAsync());
        Assert.Single(await fixture.Db.Set<ExchangeItem>().AsNoTracking().ToListAsync());
        Assert.Equal("EXCHANGING", await fixture.Db.Set<ETicket>().AsNoTracking()
            .Where(item => item.OrderItemId == 101)
            .Select(item => item.TicketStatus).SingleAsync());
    }

    [Fact]
    public async Task ApproveAsync_AcquiresAggregateLocksInDocumentedOrder()
    {
        await using var fixture = await ExchangeQuoteServiceTests.CreateFixtureAsync(
            [105m], [125m], fee: 5m);
        var application = new ExchangeApplicationService(
            fixture.Db,
            new ExchangePolicyEngine(),
            fixture.TimeProvider);
        var created = await application.CreateAsync(
            7,
            "alice",
            11,
            new CreateExchangeRequest(
                22,
                [new ExchangeTargetItemRequest(101, 701, 801, "lock-701")],
                null));
        var recorder = new RecordingExchangeLockCoordinator();
        var review = new ExchangeReviewService(
            fixture.Db,
            fixture.TimeProvider,
            recorder,
            application,
            Options.Create(new ExchangeOptions()),
            new TicketIssuanceService(new DeterministicTicketTokenService()));

        var result = await review.ApproveAsync(
            "admin",
            created.Value!.ExchangeId,
            new ApproveExchangeRequest(null));

        Assert.True(result.IsSuccess);
        var childOrderId = result.Value!.ChildOrderId;
        Assert.Equal(
            new[]
            {
                $"request:{created.Value.ExchangeId}",
                "order:11",
                $"order:{childOrderId}",
                "item:101",
                $"item:{result.Value.Items[0].NewOrderItemId}",
                "ticket:201",
                "reservation:301",
                $"reservation:{await TargetReservationIdAsync(fixture.Db, result.Value.Items[0].NewOrderItemId)}",
            },
            recorder.Calls);
    }

    private static async Task<long> TargetReservationIdAsync(
        AppDbContext dbContext,
        long newOrderItemId) =>
        await dbContext.Set<SeatReservation>().AsNoTracking()
            .Where(item => item.OrderItemId == newOrderItemId)
            .Select(item => item.SeatReservationId)
            .SingleAsync();

    private static AppDbContext CreateSharedContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<SqliteAuthDbContext>()
            .UseSqlite(connectionString)
            .Options;
        return new SqliteAuthDbContext(options);
    }

    private static ExchangeApplicationService CreateApplication(
        AppDbContext db,
        TimeProvider timeProvider) => new(
        db,
        new ExchangePolicyEngine(),
        timeProvider,
        new OracleExchangeLockCoordinator(db));

    private static ExchangeReviewService CreateReview(
        AppDbContext db,
        TimeProvider timeProvider)
    {
        var application = CreateApplication(db, timeProvider);
        return new ExchangeReviewService(
            db,
            timeProvider,
            new OracleExchangeLockCoordinator(db),
            application,
            Options.Create(new ExchangeOptions()),
            new TicketIssuanceService(new DeterministicTicketTokenService()));
    }

    private static ExchangePaymentService CreatePayment(
        AppDbContext db,
        TimeProvider timeProvider)
    {
        var application = CreateApplication(db, timeProvider);
        var issuance = new TicketIssuanceService(new DeterministicTicketTokenService());
        var review = new ExchangeReviewService(
            db,
            timeProvider,
            new OracleExchangeLockCoordinator(db),
            application,
            Options.Create(new ExchangeOptions()),
            issuance);
        return new ExchangePaymentService(
            db,
            timeProvider,
            new OracleExchangeLockCoordinator(db),
            application,
            review,
            issuance);
    }

    private static async Task AssertFailedTerminalAsync(
        SharedExchangeDatabase shared,
        int expectPayments,
        int expectNewTickets)
    {
        await using var verification = shared.CreateContext();
        var exchange = await verification.Set<ExchangeRequest>().AsNoTracking().SingleAsync();
        Assert.Equal("FAILED", exchange.ExchangeStatus);
        var child = await verification.Set<Order>().AsNoTracking()
            .SingleAsync(item => item.OrderType == "EXCHANGE");
        Assert.Equal("CANCELLED", child.OrderStatus);
        Assert.Equal(expectPayments, await verification.Set<Payment>().AsNoTracking()
            .CountAsync(item => item.OrderId == child.OrderId));
        Assert.Equal(expectNewTickets, await verification.Set<ETicket>().AsNoTracking()
            .CountAsync(item => item.OrderItemId != 101));
        Assert.Equal("UNUSED", await verification.Set<ETicket>().AsNoTracking()
            .Where(item => item.OrderItemId == 101)
            .Select(item => item.TicketStatus).SingleAsync());
        Assert.Equal("CANCELLED", await verification.Set<SeatReservation>().AsNoTracking()
            .Where(item => item.OrderItemId != 101)
            .Select(item => item.ReservationStatus).SingleAsync());
    }

    private sealed class ThrowOnSaveChangesInterceptor(int throwOnCall)
        : SaveChangesInterceptor
    {
        private int callCount;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref callCount) == throwOnCall)
                throw new DbUpdateException("Injected exchange persistence failure.");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RecordingExchangeLockCoordinator : IExchangeLockCoordinator
    {
        public List<string> Calls { get; } = [];

        public Task<bool> LockExchangeRequestAsync(long exchangeId, CancellationToken cancellationToken) =>
            RecordAsync($"request:{exchangeId}");

        public Task<bool> LockOrderAsync(long orderId, CancellationToken cancellationToken) =>
            RecordAsync($"order:{orderId}");

        public Task<bool> LockOrderItemAsync(long orderItemId, CancellationToken cancellationToken) =>
            RecordAsync($"item:{orderItemId}");

        public Task<bool> LockETicketAsync(long eTicketId, CancellationToken cancellationToken) =>
            RecordAsync($"ticket:{eTicketId}");

        public Task<bool> LockSeatReservationAsync(
            long seatReservationId,
            CancellationToken cancellationToken) =>
            RecordAsync($"reservation:{seatReservationId}");

        public Task<bool> LockSeatLockAsync(long seatLockId, CancellationToken cancellationToken) =>
            RecordAsync($"seat-lock:{seatLockId}");

        private Task<bool> RecordAsync(string value)
        {
            Calls.Add(value);
            return Task.FromResult(true);
        }
    }

    private sealed class TwoPartyAsyncBarrier
    {
        private readonly TaskCompletionSource release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;

        public async Task SignalAndWaitAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref arrivals) == 2)
                release.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class ExistingTicketTokenService(
        string ticketNo,
        string expectedQrCode) : ITicketTokenService
    {
        public TicketCredential Generate(DateTimeOffset issuedAt) =>
            throw new NotSupportedException();

        public bool TryValidate(string qrCode, out TicketTokenPayload? payload)
        {
            payload = string.Equals(qrCode, expectedQrCode, StringComparison.Ordinal)
                ? new TicketTokenPayload(ticketNo, 0, "concurrency")
                : null;
            return payload is not null;
        }
    }

    private sealed class SharedExchangeDatabase : IAsyncDisposable
    {
        private readonly string connectionString;
        private readonly SqliteConnection keeper;

        private SharedExchangeDatabase(
            string connectionString,
            SqliteConnection keeper)
        {
            this.connectionString = connectionString;
            this.keeper = keeper;
        }

        public static async Task<SharedExchangeDatabase> CreateAsync(RefundTestData source)
        {
            var databaseName = $"exchange-race-{Guid.NewGuid():N}";
            var connectionString =
                $"Data Source={databaseName};Mode=Memory;Cache=Shared;Default Timeout=30;" +
                "Pooling=False;Foreign Keys=False";
            var keeper = new SqliteConnection(connectionString);
            await keeper.OpenAsync();
            source.BackupTo(keeper);
            return new SharedExchangeDatabase(connectionString, keeper);
        }

        public AppDbContext CreateContext() => CreateSharedContext(connectionString);

        public ValueTask DisposeAsync() => keeper.DisposeAsync();
    }

    private sealed class DeterministicTicketTokenService : ITicketTokenService
    {
        public TicketCredential Generate(DateTimeOffset issuedAt) =>
            new("EX-CONCURRENCY-TICKET", "EX-CONCURRENCY-ANTI", "EX-CONCURRENCY-QR");

        public bool TryValidate(string qrCode, out TicketTokenPayload? payload)
        {
            payload = null;
            return false;
        }
    }
}
