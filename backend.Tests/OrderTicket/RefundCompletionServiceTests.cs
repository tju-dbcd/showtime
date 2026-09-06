using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Entities.SeatZone;
using ShowtimeBackend.Services.OrderTicket;
using ShowtimeBackend.Services.OrderTicket.Messaging;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class RefundCompletionServiceTests
{
    [Fact]
    public async Task CompleteAsync_ApprovedRefund_AtomicallyCompletesPersistedWorkflow()
    {
        await using var fixture = await RefundTestData.CreateIssuedOrderAsync(
            totalAmount: 105m,
            payAmount: 105m,
            itemPrices: [105m]);
        await SeedPendingRefundAsync(fixture);
        var approvedEvent = await fixture.ApproveAndReadEventAsync();
        var audit = new RecordingAuditSink(fixture.Db);
        var service = new RefundCompletionService(
            fixture.Db,
            fixture.TimeProvider,
            new TestRefundLockCoordinator(fixture.Db),
            NullLogger<RefundCompletionService>.Instance,
            audit);

        var result = await service.CompleteAsync(
            approvedEvent,
            CancellationToken.None);

        Assert.Equal(RefundCompletionOutcome.Completed, result.Outcome);
        Assert.Equal(84m, await fixture.PaymentRefundAmountAsync());
        Assert.Equal("REFUNDED", await fixture.ItemStatusAsync());
        Assert.Equal("REFUNDED", await fixture.TicketStatusAsync());
        Assert.Equal("RELEASED", await fixture.ReservationStatusAsync());
        Assert.Equal("REFUNDED", await fixture.OrderStatusAsync());
        var refund = await fixture.Db.Set<RefundRequest>().AsNoTracking().SingleAsync();
        Assert.Equal("APPROVED", refund.ApproveStatus);
        Assert.Equal("COMPLETED", refund.RefundStatus);
        Assert.Equal(RefundTestData.FixedUtcNow, refund.CompleteTime);
        Assert.Equal(RefundCompletionService.SystemActor, refund.UpdateBy);
        Assert.Contains(audit.Events, item => item.Operation == "REFUND_COMPLETED" &&
            item.Metadata!["EventId"] == approvedEvent.EventId);
        Assert.True(audit.ObservedWithoutTransaction);
    }

    [Fact]
    public async Task CompleteAsync_ReplayedEvents_DoNotRefundTwice()
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync();
        await MakeFinancialsConsistentAsync(fixture);
        var approvedEvent = await fixture.ApproveAndReadEventAsync();
        var first = await fixture.CreateCompletionService().CompleteAsync(
            approvedEvent,
            CancellationToken.None);
        var replay = approvedEvent with { EventId = Guid.NewGuid().ToString("D") };
        var second = await fixture.CreateCompletionService().CompleteAsync(
            replay,
            CancellationToken.None);

        Assert.Equal(RefundCompletionOutcome.Completed, first.Outcome);
        Assert.Equal(RefundCompletionOutcome.AlreadyCompleted, second.Outcome);
        Assert.Equal(84m, await fixture.PaymentRefundAmountAsync());
        Assert.Equal("RELEASED", await fixture.ReservationStatusAsync());
    }

    [Theory]
    [InlineData("order")]
    [InlineData("user")]
    [InlineData("amount")]
    public async Task CompleteAsync_EventIdentityOrAmountMismatch_IsPermanentAndDoesNotMutate(
        string mismatch)
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync();
        await MakeFinancialsConsistentAsync(fixture);
        var approvedEvent = await fixture.ApproveAndReadEventAsync();
        approvedEvent = mismatch switch
        {
            "order" => approvedEvent with { OrderId = approvedEvent.OrderId + 1 },
            "user" => approvedEvent with { UserId = approvedEvent.UserId + 1 },
            _ => approvedEvent with { ActualRefund = approvedEvent.ActualRefund + 1m },
        };

        var result = await fixture.CreateCompletionService().CompleteAsync(
            approvedEvent,
            CancellationToken.None);

        Assert.Equal(RefundCompletionOutcome.PermanentFailure, result.Outcome);
        Assert.Equal(0m, await fixture.PaymentRefundAmountAsync());
        Assert.Equal("PROCESSING", await RefundStatusAsync(fixture));
        Assert.Equal("ACTIVE", await fixture.ReservationStatusAsync());
        Assert.Equal("REFUNDING", await fixture.ItemStatusAsync());
    }

    [Fact]
    public async Task CompleteAsync_SaveFailure_RollsBackPaymentReservationAndAggregate()
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync();
        await MakeFinancialsConsistentAsync(fixture);
        var approvedEvent = await fixture.ApproveAndReadEventAsync();
        await using var failingDb = fixture.CreateDbContext(new FailCompletionSaveInterceptor());

        var result = await fixture.CreateCompletionService(failingDb).CompleteAsync(
            approvedEvent,
            CancellationToken.None);

        Assert.Equal(RefundCompletionOutcome.RetryableFailure, result.Outcome);
        Assert.Equal(0m, await fixture.PaymentRefundAmountAsync());
        Assert.Equal("PROCESSING", await RefundStatusAsync(fixture));
        Assert.Equal("ACTIVE", await fixture.ReservationStatusAsync());
        Assert.Equal("REFUNDING", await fixture.ItemStatusAsync());
        Assert.Equal("REFUNDING", await fixture.TicketStatusAsync());
        Assert.Equal("ISSUED", await fixture.OrderStatusAsync());
    }

    [Fact]
    public async Task CompleteAsync_AuditFailure_DoesNotUndoCommittedRefund()
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync();
        await MakeFinancialsConsistentAsync(fixture);
        var approvedEvent = await fixture.ApproveAndReadEventAsync();
        var service = new RefundCompletionService(
            fixture.Db,
            fixture.TimeProvider,
            new TestRefundLockCoordinator(fixture.Db),
            NullLogger<RefundCompletionService>.Instance,
            new ThrowingAuditSink());

        var result = await service.CompleteAsync(approvedEvent, CancellationToken.None);

        Assert.Equal(RefundCompletionOutcome.Completed, result.Outcome);
        Assert.Equal(84m, await fixture.PaymentRefundAmountAsync());
        Assert.Equal("COMPLETED", await RefundStatusAsync(fixture));
        Assert.Equal("RELEASED", await fixture.ReservationStatusAsync());
    }

    [Fact]
    public async Task ApproveAsync_OutboxSaveFailure_RollsBackReviewAndOutbox()
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync();
        await MakeFinancialsConsistentAsync(fixture);
        await using var failingDb = fixture.CreateDbContext(new FailOutboxSaveInterceptor());
        var service = new RefundReviewService(
            failingDb,
            fixture.TimeProvider,
            new TestRefundLockCoordinator(failingDb),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RefundReviewService>.Instance,
            fixture.AuditSink);

        var result = await service.ApproveAsync(
            "admin",
            fixture.RefundId,
            new ApproveRefundRequest(null),
            CancellationToken.None);

        Assert.Equal(OrderTicketFailure.Internal, result.Failure);
        Assert.Equal("PENDING", await fixture.RefundApproveStatusAsync());
        Assert.Equal("PENDING", await RefundStatusAsync(fixture));
        var refund = await fixture.Db.Set<RefundRequest>().AsNoTracking().SingleAsync();
        Assert.Null(refund.ReviewBy);
        Assert.Null(refund.ReviewTime);
        Assert.Empty(await fixture.Db.Set<OrderEventOutbox>().AsNoTracking().ToListAsync());
        Assert.Equal(0m, await fixture.PaymentRefundAmountAsync());
        Assert.Equal("ACTIVE", await fixture.ReservationStatusAsync());
    }

    [Fact]
    public async Task CompleteAsync_TwoPartialRefunds_ProgressesOrderAndAccumulatesExactlyOnce()
    {
        await using var fixture = await RefundTestData.CreateTwoPendingRefundsAsync();
        var firstEvent = await fixture.ApproveAndReadEventAsync(fixture.RefundIds[0]);
        var first = await fixture.CreateCompletionService().CompleteAsync(
            firstEvent,
            CancellationToken.None);
        var afterFirst = await fixture.OrderStatusAsync();
        var secondEvent = await fixture.ApproveAndReadEventAsync(fixture.RefundIds[1]);
        var second = await fixture.CreateCompletionService().CompleteAsync(
            secondEvent,
            CancellationToken.None);

        Assert.Equal(RefundCompletionOutcome.Completed, first.Outcome);
        Assert.Equal("PART_REFUND", afterFirst);
        Assert.Equal(RefundCompletionOutcome.Completed, second.Outcome);
        Assert.Equal("REFUNDED", await fixture.OrderStatusAsync());
        Assert.Equal(168m, await fixture.PaymentRefundAmountAsync());
    }

    [Fact]
    public async Task CompleteAsync_ConcurrentDeliveries_OnlyOneMutationWinsAndRetryIsIdempotent()
    {
        await using var database = await RefundTestData.CreateSharedSqliteAsync();
        await using (var seedDb = database.CreateContext())
        {
            await seedDb.Database.OpenConnectionAsync();
            await seedDb.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
            seedDb.Add(new Payment
            {
                PaymentId = 31,
                PaymentNo = "PAY000031",
                OrderId = 11,
                UserId = 7,
                PayAmount = 105m,
                PayChannel = "ALIPAY",
                PayStatus = "SUCCESS",
                PayTime = RefundTestData.FixedUtcNow.AddHours(-2),
            });
            seedDb.Add(new SeatReservation
            {
                SeatReservationId = 301,
                SessionId = 21,
                SeatId = 501,
                OrderItemId = 101,
                ReservationType = "ORDER",
                ReservationStatus = "ACTIVE",
                ReserveTime = RefundTestData.FixedUtcNow.AddHours(-3),
            });
            var refund = await seedDb.Set<RefundRequest>().SingleAsync();
            refund.ApproveStatus = "APPROVED";
            refund.RefundStatus = "PROCESSING";
            await seedDb.SaveChangesAsync();
        }

        var approvedEvent = new RefundApprovedEvent(
            Guid.NewGuid().ToString("D"),
            RefundApprovedEvent.TypeName,
            RefundTestData.FixedUtcNow,
            401,
            "REF000401",
            11,
            7,
            84m);
        await using var firstDb = database.CreateContext();
        await using var secondDb = database.CreateContext();
        firstDb.Database.SetCommandTimeout(1);
        secondDb.Database.SetCommandTimeout(1);
        var firstService = CreateService(firstDb);
        var secondService = CreateService(secondDb);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var results = await Task.WhenAll(
            firstService.CompleteAsync(approvedEvent, timeout.Token),
            secondService.CompleteAsync(approvedEvent, timeout.Token));

        Assert.Equal(1, results.Count(item => item.Outcome == RefundCompletionOutcome.Completed));
        Assert.Contains(results, item => item.Outcome is
            RefundCompletionOutcome.AlreadyCompleted or RefundCompletionOutcome.RetryableFailure);
        await using var retryDb = database.CreateContext();
        var retry = await CreateService(retryDb).CompleteAsync(
            approvedEvent,
            CancellationToken.None);
        Assert.Equal(RefundCompletionOutcome.AlreadyCompleted, retry.Outcome);
        await using var verificationDb = database.CreateContext();
        Assert.Equal(84m, await verificationDb.Set<Payment>()
            .Select(item => item.RefundAmount)
            .SingleAsync());
        Assert.Equal("RELEASED", await verificationDb.Set<SeatReservation>()
            .Select(item => item.ReservationStatus)
            .SingleAsync());
    }

    [Fact]
    public void RefundApprovedEvent_SerializesStableCamelCaseContract()
    {
        var approvedEvent = new RefundApprovedEvent(
            "91aeb9e7-8b57-4ad4-9841-d46ee24d3611",
            RefundApprovedEvent.TypeName,
            RefundTestData.FixedUtcNow,
            401,
            "REF000401",
            11,
            7,
            84m);

        using var document = JsonDocument.Parse(approvedEvent.Serialize());
        var root = document.RootElement;
        Assert.Equal(approvedEvent.EventId, root.GetProperty("eventId").GetString());
        Assert.Equal(RefundApprovedEvent.TypeName, root.GetProperty("eventType").GetString());
        Assert.Equal(401, root.GetProperty("refundId").GetInt64());
        Assert.Equal(84m, root.GetProperty("actualRefund").GetDecimal());
        Assert.False(root.TryGetProperty("EventId", out _));
    }

    private static async Task SeedPendingRefundAsync(RefundTestData fixture)
    {
        fixture.Db.ChangeTracker.Clear();
        var item = await fixture.Db.Set<OrderItem>().Include(value => value.ETicket).FirstAsync();
        item.ItemStatus = "REFUNDING";
        item.ETicket!.TicketStatus = "REFUNDING";
        fixture.Db.Add(new RefundRequest
        {
            RefundId = 401,
            RefundNo = "REF000401",
            OrderId = fixture.OrderId,
            UserId = fixture.UserId,
            RefundType = "PART",
            RefundReason = "测试",
            RefundAmount = 105m,
            ActualRefund = 84m,
            FeeRate = 0.8m,
            AppliedServiceFee = 0m,
            ApproveStatus = "PENDING",
            RefundStatus = "PENDING",
            CreateTime = RefundTestData.FixedUtcNow.AddHours(-1),
            CreateBy = "test",
            UpdateBy = "test",
            Items =
            [
                new RefundItem
                {
                    RefundItemId = 501,
                    OrderItemId = item.OrderItemId,
                    RefundBaseAmount = 105m,
                    CreateBy = "test",
                    UpdateBy = "test",
                },
            ],
        });
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        fixture.SetRefundId(401);
    }

    private static Task<string> RefundStatusAsync(RefundTestData fixture) => fixture.Db
        .Set<RefundRequest>()
        .AsNoTracking()
        .Where(item => item.RefundId == fixture.RefundId)
        .Select(item => item.RefundStatus)
        .SingleAsync();

    private static async Task MakeFinancialsConsistentAsync(RefundTestData fixture)
    {
        var total = await fixture.Db.Set<OrderItem>().SumAsync(item => item.UnitPrice);
        var order = await fixture.Db.Set<Order>().SingleAsync();
        var payment = await fixture.Db.Set<Payment>().SingleAsync();
        order.TotalAmount = total;
        order.DiscountAmount = 0m;
        payment.PayAmount = total;
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
    }

    private static RefundCompletionService CreateService(AppDbContext db) => new(
        db,
        new FixedTimeProvider(RefundTestData.FixedUtcNow),
        new TestRefundLockCoordinator(db),
        NullLogger<RefundCompletionService>.Instance,
        new NullOrderTicketAuditSink());

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }

    private sealed class FailCompletionSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<RefundRequest>()
                .Any(item => item.Entity.RefundStatus == "COMPLETED"))
            {
                throw new DbUpdateException("Injected completion save failure.");
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class FailOutboxSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<OrderEventOutbox>()
                .Any(item => item.State == EntityState.Added))
            {
                throw new DbUpdateException("Injected outbox save failure.");
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class RecordingAuditSink(AppDbContext? db = null) : IOrderTicketAuditSink
    {
        public List<OrderTicketAuditEvent> Events { get; } = [];
        public bool ObservedWithoutTransaction { get; private set; }

        public ValueTask WriteAsync(
            OrderTicketAuditEvent auditEvent,
            CancellationToken cancellationToken)
        {
            Events.Add(auditEvent);
            ObservedWithoutTransaction = db?.Database.CurrentTransaction is null;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingAuditSink : IOrderTicketAuditSink
    {
        public ValueTask WriteAsync(
            OrderTicketAuditEvent auditEvent,
            CancellationToken cancellationToken) =>
            ValueTask.FromException(new InvalidOperationException("Injected audit failure."));
    }
}
