using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ShowtimeBackend.Common;
using ShowtimeBackend.Common.TicketSecurity;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Entities.SeatZone;
using ShowtimeBackend.Entities.ShowSession;
using ShowtimeBackend.Services.OrderTicket;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class ExchangeQuoteServiceTests
{
    [Fact]
    public async Task QuoteAsync_UsesExplicitPairingAndAddsFeeToPriceDifference()
    {
        await using var fixture = await CreateFixtureAsync([105m, 205m], [125m, 225m], fee: 8m);
        var request = new ExchangeQuoteRequest(22,
        [
            new(102, 702, 802, "lock-702"),
            new(101, 701, 801, "lock-701"),
        ]);

        var result = await CreateService(fixture).QuoteAsync(7, 11, request);

        Assert.True(result.IsSuccess);
        Assert.Equal(310m, result.Value!.OrigDeduction);
        Assert.Equal(350m, result.Value.TargetAmount);
        Assert.Equal(40m, result.Value.PriceDiff);
        Assert.Equal(8m, result.Value.ExchangeFee);
        Assert.Equal(48m, result.Value.AmountDue);
        Assert.Equal(102, result.Value.Items[0].OriginalOrderItemId);
        Assert.Equal(702, result.Value.Items[0].TargetSeatId);
        Assert.Equal(205m, result.Value.Items[0].OriginalUnitPrice);
        Assert.Equal(225m, result.Value.Items[0].NewUnitPrice);
        Assert.Equal(101, result.Value.Items[1].OriginalOrderItemId);
        Assert.Equal(701, result.Value.Items[1].TargetSeatId);
    }

    [Fact]
    public async Task QuoteAsync_UsesTargetLockCreateTimeForDynamicPricing()
    {
        await using var fixture = await CreateFixtureAsync([105m], [150m], fee: 0m);
        fixture.Db.Add(new DynamicPricingRule
        {
            DynamicPricingRuleId = 901,
            SessionId = 22,
            SeatSectionId = 40,
            RuleName = "48-hour fixed price",
            TriggerType = "TIME_WINDOW",
            StartOffsetMinutes = 2_880,
            EndOffsetMinutes = 0,
            AdjustmentType = "FIXED_PRICE",
            AdjustmentValue = 130m,
            Priority = 1,
            Status = "ENABLED",
        });
        await fixture.Db.SaveChangesAsync();

        var result = await CreateService(fixture).QuoteAsync(
            7, 11, new ExchangeQuoteRequest(22, [new(101, 701, 801, "lock-701")]));

        Assert.True(result.IsSuccess);
        Assert.Equal(130m, result.Value!.TargetAmount);
        Assert.Equal(25m, result.Value.AmountDue);
    }

    [Fact]
    public async Task QuoteAsync_RejectsPriceDecrease()
    {
        await using var fixture = await CreateFixtureAsync([105m], [100m], fee: 10m);

        var result = await CreateService(fixture).QuoteAsync(
            7, 11, new ExchangeQuoteRequest(22, [new(101, 701, 801, "lock-701")]));

        Assert.False(result.IsSuccess);
        Assert.Equal("EXCHANGE_PRICE_DOWN_NOT_SUPPORTED", result.ErrorCode);
    }

    [Fact]
    public async Task QuoteAsync_RejectsDuplicateOriginalItemMapping()
    {
        await using var fixture = await CreateFixtureAsync([105m, 205m], [125m, 225m], fee: 8m);

        var result = await CreateService(fixture).QuoteAsync(7, 11,
            new ExchangeQuoteRequest(22,
            [new(101, 701, 801, "lock-701"), new(101, 702, 802, "lock-702")]));

        Assert.False(result.IsSuccess);
        Assert.Equal("EXCHANGE_REQUEST_INVALID", result.ErrorCode);
    }

    [Fact]
    public async Task QuoteAsync_RejectsWrongLockOwnerOrToken()
    {
        await using var fixture = await CreateFixtureAsync([105m], [125m], fee: 0m);

        var result = await CreateService(fixture).QuoteAsync(
            7, 11, new ExchangeQuoteRequest(22, [new(101, 701, 801, "wrong")]));

        Assert.False(result.IsSuccess);
        Assert.Equal("EXCHANGE_SEAT_LOCK_INVALID", result.ErrorCode);
    }

    [Fact]
    public async Task QuoteAsync_RejectsCompletedExchangeHistory()
    {
        await using var fixture = await CreateFixtureAsync([105m], [125m], fee: 0m);
        fixture.Db.Add(new ExchangeRequest
        {
            ExchangeId = 501,
            ExchangeNo = "EX000501",
            OrderId = 11,
            UserId = 7,
            OrigSessionId = 21,
            TargetSessionId = 22,
            ExchangeFee = 0m,
            PriceDiff = 20m,
            ApproveStatus = "APPROVED",
            ExchangeStatus = "COMPLETED",
        });
        fixture.Db.Add(new ExchangeItem
        {
            ExchangeItemId = 601,
            ExchangeId = 501,
            OrderItemId = 101,
            NewOrderItemId = 999,
        });
        await fixture.Db.SaveChangesAsync();

        var result = await CreateService(fixture).QuoteAsync(
            7, 11, new ExchangeQuoteRequest(22, [new(101, 701, 801, "lock-701")]));

        Assert.False(result.IsSuccess);
        Assert.Equal("EXCHANGE_TICKET_HISTORY_CONFLICT", result.ErrorCode);
    }

    [Fact]
    public async Task QuoteAsync_FailedExchangeHistoryAllowsOriginalTicketAfterRelock()
    {
        await using var fixture = await CreateFixtureAsync([105m], [125m], fee: 0m);
        var application = CreateService(fixture);
        var created = await application.CreateAsync(7, "alice", 11,
            new CreateExchangeRequest(22, [new(101, 701, 801, "lock-701")], null));
        var review = new ExchangeReviewService(
            fixture.Db, fixture.TimeProvider,
            new OracleExchangeLockCoordinator(fixture.Db), application);
        var rejected = await review.RejectAsync(
            "admin", created.Value!.ExchangeId, new RejectExchangeRequest("retry"));
        Assert.True(rejected.IsSuccess);
        var seatLock = await fixture.Db.Set<SeatLock>()
            .SingleAsync(item => item.SeatLockId == 1001);
        seatLock.LockStatus = "ACTIVE";
        seatLock.LockToken = "lock-reacquired";
        seatLock.ExpireTime = RefundTestData.FixedUtcNow.AddMinutes(10);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var result = await application.QuoteAsync(
            7, 11, new ExchangeQuoteRequest(
                22, [new(101, 701, 801, "lock-reacquired")]));

        Assert.True(result.IsSuccess, result.Message);
    }

    [Fact]
    public async Task CreateAsync_FreezesOriginalAndConvertsTargetResourcesAtomically()
    {
        await using var fixture = await CreateFixtureAsync([105m], [125m], fee: 8m);
        var service = CreateService(fixture);

        var result = await service.CreateAsync(7, "alice", 11,
            new CreateExchangeRequest(22, [new(101, 701, 801, "lock-701")], "change"));

        Assert.True(result.IsSuccess);
        Assert.Equal(28m, result.Value!.AmountDue);
        Assert.Equal(ShowtimeBackend.Common.ExchangeApproveStatus.PENDING, result.Value.ApproveStatus);
        Assert.Equal(ShowtimeBackend.Common.ExchangeStatus.PENDING, result.Value.ExchangeStatus);
        Assert.Equal(RefundTestData.FixedUtcNow.AddMinutes(30), result.Value.ExpireTime);
        Assert.Equal("EXCHANGING", (await fixture.Db.Set<OrderItem>().SingleAsync(i => i.OrderItemId == 101)).ItemStatus);
        Assert.Equal("EXCHANGING", (await fixture.Db.Set<ETicket>().SingleAsync(i => i.OrderItemId == 101)).TicketStatus);
        Assert.Equal("CONVERTED", (await fixture.Db.Set<SeatLock>().SingleAsync(i => i.SeatId == 701)).LockStatus);
        var child = await fixture.Db.Set<Order>().SingleAsync(i => i.OrderType == "EXCHANGE");
        Assert.Equal("PENDING_PAY", child.OrderStatus);
        Assert.Equal(28m, child.TotalAmount);
        Assert.Equal(11, child.ParentOrderId);
        Assert.Equal("ACTIVE", (await fixture.Db.Set<SeatReservation>()
            .SingleAsync(i => i.OrderItemId == result.Value.Items[0].NewOrderItemId)).ReservationStatus);
    }

    [Fact]
    public async Task RejectAsync_RestoresOriginalAndCancelsChildAndTargetReservation()
    {
        await using var fixture = await CreateFixtureAsync([105m], [125m], fee: 0m);
        var application = CreateService(fixture);
        var created = await application.CreateAsync(7, "alice", 11,
            new CreateExchangeRequest(22, [new(101, 701, 801, "lock-701")], null));
        var review = new ExchangeReviewService(
            fixture.Db, fixture.TimeProvider, new OracleExchangeLockCoordinator(fixture.Db), application);

        var result = await review.RejectAsync("admin", created.Value!.ExchangeId, new("no"));

        Assert.True(result.IsSuccess);
        Assert.Equal(ShowtimeBackend.Common.ExchangeApproveStatus.REJECTED, result.Value!.ApproveStatus);
        Assert.Equal(ShowtimeBackend.Common.ExchangeStatus.FAILED, result.Value.ExchangeStatus);
        Assert.Equal("NORMAL", (await fixture.Db.Set<OrderItem>().SingleAsync(i => i.OrderItemId == 101)).ItemStatus);
        Assert.Equal("UNUSED", (await fixture.Db.Set<ETicket>().SingleAsync(i => i.OrderItemId == 101)).TicketStatus);
        Assert.Equal("CANCELLED", (await fixture.Db.Set<Order>().SingleAsync(i => i.OrderType == "EXCHANGE")).OrderStatus);
        Assert.Equal("CANCELLED", (await fixture.Db.Set<SeatReservation>()
            .SingleAsync(i => i.OrderItemId == result.Value.Items[0].NewOrderItemId)).ReservationStatus);
    }

    [Fact]
    public async Task ExpirationService_AtExactExpiry_RestoresPendingExchange()
    {
        await using var fixture = await CreateFixtureAsync([105m], [125m], fee: 0m);
        var application = CreateService(fixture);
        var created = await application.CreateAsync(7, "alice", 11,
            new CreateExchangeRequest(22, [new(101, 701, 801, "lock-701")], null));
        var child = await fixture.Db.Set<Order>().SingleAsync(i => i.OrderType == "EXCHANGE");
        child.ExpireTime = RefundTestData.FixedUtcNow;
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var review = new ExchangeReviewService(
            fixture.Db, fixture.TimeProvider, new OracleExchangeLockCoordinator(fixture.Db), application);
        var expiration = new ExchangeExpirationService(
            fixture.Db, fixture.TimeProvider, review, Options.Create(new ExchangeOptions()),
            NullLogger<ExchangeExpirationService>.Instance);

        var processed = await expiration.ExpireDueBatchAsync();

        Assert.Equal(1, processed.CandidateCount);
        Assert.Equal(1, processed.SuccessCount);
        var result = await application.GetAsync(7, created.Value!.ExchangeId);
        Assert.Equal(ShowtimeBackend.Common.ExchangeApproveStatus.REJECTED, result.Value!.ApproveStatus);
        Assert.Equal(ShowtimeBackend.Common.ExchangeStatus.FAILED, result.Value.ExchangeStatus);
        Assert.Equal("UNUSED", (await fixture.Db.Set<ETicket>().SingleAsync(i => i.OrderItemId == 101)).TicketStatus);
    }

    [Fact]
    public async Task ExpirationService_AtExactExpiry_RestoresApprovedProcessingExchange()
    {
        await using var fixture = await CreateFixtureAsync([105m], [125m], fee: 5m);
        var application = CreateService(fixture);
        var created = await application.CreateAsync(7, "alice", 11,
            new CreateExchangeRequest(22, [new(101, 701, 801, "lock-701")], null));
        var issuance = new TicketIssuanceService(new SequenceTicketTokenService());
        var review = new ExchangeReviewService(
            fixture.Db, fixture.TimeProvider, new OracleExchangeLockCoordinator(fixture.Db), application,
            Options.Create(new ExchangeOptions()), issuance);
        var approved = await review.ApproveAsync("admin", created.Value!.ExchangeId, new(null));
        Assert.Equal(ExchangeStatus.PROCESSING, approved.Value!.ExchangeStatus);
        var child = await fixture.Db.Set<Order>().SingleAsync(i => i.OrderType == "EXCHANGE");
        child.ExpireTime = RefundTestData.FixedUtcNow;
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var expiration = new ExchangeExpirationService(
            fixture.Db, fixture.TimeProvider, review, Options.Create(new ExchangeOptions()),
            NullLogger<ExchangeExpirationService>.Instance);

        var processed = await expiration.ExpireDueBatchAsync();

        Assert.Equal(1, processed.CandidateCount);
        Assert.Equal(1, processed.SuccessCount);
        var result = await application.GetAsync(7, created.Value.ExchangeId);
        Assert.Equal(ExchangeApproveStatus.APPROVED, result.Value!.ApproveStatus);
        Assert.Equal(ExchangeStatus.FAILED, result.Value.ExchangeStatus);
        Assert.Equal("UNUSED", (await fixture.Db.Set<ETicket>()
            .SingleAsync(i => i.OrderItemId == 101)).TicketStatus);
        Assert.Equal("CANCELLED", (await fixture.Db.Set<SeatReservation>()
            .SingleAsync(i => i.OrderItemId == result.Value.Items[0].NewOrderItemId)).ReservationStatus);
    }

    [Fact]
    public async Task ApproveAsync_ZeroAmount_CreatesZeroPaymentAndCompletesExchange()
    {
        await using var fixture = await CreateFixtureAsync([105m], [105m], fee: 0m);
        var application = CreateService(fixture);
        var created = await application.CreateAsync(7, "alice", 11,
            new CreateExchangeRequest(22, [new(101, 701, 801, "lock-701")], null));
        var issuance = new TicketIssuanceService(new SequenceTicketTokenService());
        var review = new ExchangeReviewService(
            fixture.Db, fixture.TimeProvider, new OracleExchangeLockCoordinator(fixture.Db), application,
            Options.Create(new ExchangeOptions()), issuance);

        var result = await review.ApproveAsync("admin", created.Value!.ExchangeId, new(null));

        Assert.True(result.IsSuccess);
        Assert.Equal(ExchangeApproveStatus.APPROVED, result.Value!.ApproveStatus);
        Assert.Equal(ExchangeStatus.COMPLETED, result.Value.ExchangeStatus);
        var child = await fixture.Db.Set<Order>().Include(i => i.Payments)
            .SingleAsync(i => i.OrderType == "EXCHANGE");
        var payment = Assert.Single(child.Payments);
        Assert.Equal(0m, payment.PayAmount);
        Assert.Equal("SUCCESS", payment.PayStatus);
        Assert.Equal("ISSUED", child.OrderStatus);
        Assert.Equal("EXCHANGED", (await fixture.Db.Set<ETicket>().SingleAsync(i => i.OrderItemId == 101)).TicketStatus);
        Assert.Equal("UNUSED", (await fixture.Db.Set<ETicket>()
            .SingleAsync(i => i.OrderItemId == result.Value.Items[0].NewOrderItemId)).TicketStatus);
    }

    [Fact]
    public async Task ApproveAsync_ZeroAmount_RetriesTicketIdentifierCollisionInFreshTransaction()
    {
        await using var fixture = await CreateFixtureAsync([105m], [105m], fee: 0m);
        var application = CreateService(fixture);
        var created = await application.CreateAsync(7, "alice", 11,
            new CreateExchangeRequest(22, [new(101, 701, 801, "lock-701")], null));
        var original = await fixture.Db.Set<ETicket>().SingleAsync(i => i.OrderItemId == 101);
        var issuance = new TicketIssuanceService(new SequenceTicketTokenService(
            new TicketCredential(original.ETicketNo, "collision-anti", "collision-qr"),
            new TicketCredential("EXTKT-RETRY", "retry-anti", "retry-qr")));
        var review = new ExchangeReviewService(
            fixture.Db, fixture.TimeProvider, new OracleExchangeLockCoordinator(fixture.Db), application,
            Options.Create(new ExchangeOptions()), issuance);

        var result = await review.ApproveAsync("admin", created.Value!.ExchangeId, new(null));

        Assert.True(result.IsSuccess);
        Assert.Equal(ExchangeStatus.COMPLETED, result.Value!.ExchangeStatus);
        Assert.Equal("EXTKT-RETRY", (await fixture.Db.Set<ETicket>()
            .SingleAsync(i => i.OrderItemId == result.Value.Items[0].NewOrderItemId)).ETicketNo);
        Assert.Single(await fixture.Db.Set<Payment>().Where(i => i.OrderId == result.Value.ChildOrderId).ToListAsync());
    }

    [Fact]
    public async Task PayAsync_FailedAttemptKeepsProcessing_ThenSuccessCompletesExchange()
    {
        await using var fixture = await CreateFixtureAsync([105m], [125m], fee: 5m);
        var application = CreateService(fixture);
        var created = await application.CreateAsync(7, "alice", 11,
            new CreateExchangeRequest(22, [new(101, 701, 801, "lock-701")], null));
        var issuance = new TicketIssuanceService(new SequenceTicketTokenService());
        var review = new ExchangeReviewService(
            fixture.Db, fixture.TimeProvider, new OracleExchangeLockCoordinator(fixture.Db), application,
            Options.Create(new ExchangeOptions()), issuance);
        var approved = await review.ApproveAsync("admin", created.Value!.ExchangeId, new(null));
        Assert.Equal(ExchangeStatus.PROCESSING, approved.Value!.ExchangeStatus);
        Assert.Equal(25m, approved.Value.AmountDue);
        var paymentService = new ExchangePaymentService(
            fixture.Db, fixture.TimeProvider, new OracleExchangeLockCoordinator(fixture.Db),
            application, review, issuance);

        var failed = await paymentService.PayAsync(7, "alice", created.Value.ExchangeId,
            new(PaymentChannel.ALIPAY, PaymentResult.FAIL));
        Assert.True(failed.IsSuccess);
        Assert.Equal(PaymentStatus.FAIL, failed.Value!.Payment.PayStatus);
        Assert.Equal(ExchangeStatus.PROCESSING, failed.Value.Exchange.ExchangeStatus);

        var succeeded = await paymentService.PayAsync(7, "alice", created.Value.ExchangeId,
            new(PaymentChannel.WECHAT, PaymentResult.SUCCESS));
        Assert.True(succeeded.IsSuccess);
        Assert.Equal(PaymentStatus.SUCCESS, succeeded.Value!.Payment.PayStatus);
        Assert.Equal(ExchangeStatus.COMPLETED, succeeded.Value.Exchange.ExchangeStatus);
        Assert.Equal("EXCHANGED", (await fixture.Db.Set<ETicket>().SingleAsync(i => i.OrderItemId == 101)).TicketStatus);
        Assert.Equal("RELEASED", (await fixture.Db.Set<SeatReservation>()
            .SingleAsync(i => i.OrderItemId == 101)).ReservationStatus);
        Assert.Equal("ACTIVE", (await fixture.Db.Set<SeatReservation>()
            .SingleAsync(i => i.OrderItemId == succeeded.Value.Exchange.Items[0].NewOrderItemId)).ReservationStatus);
    }

    [Fact]
    public async Task ApproveAsync_ExpiredReview_RestoresBeforeReturningExpiredError()
    {
        await using var fixture = await CreateFixtureAsync([105m], [125m], fee: 0m);
        var application = CreateService(fixture);
        var created = await application.CreateAsync(7, "alice", 11,
            new CreateExchangeRequest(22, [new(101, 701, 801, "lock-701")], null));
        var child = await fixture.Db.Set<Order>().SingleAsync(i => i.OrderType == "EXCHANGE");
        child.ExpireTime = RefundTestData.FixedUtcNow;
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var review = new ExchangeReviewService(
            fixture.Db, fixture.TimeProvider, new OracleExchangeLockCoordinator(fixture.Db), application,
            Options.Create(new ExchangeOptions()), new TicketIssuanceService(new SequenceTicketTokenService()));

        var result = await review.ApproveAsync("admin", created.Value!.ExchangeId, new(null));

        Assert.False(result.IsSuccess);
        Assert.Equal("EXCHANGE_REVIEW_EXPIRED", result.ErrorCode);
        var restored = await application.GetAsync(7, created.Value.ExchangeId);
        Assert.Equal(ExchangeApproveStatus.REJECTED, restored.Value!.ApproveStatus);
        Assert.Equal(ExchangeStatus.FAILED, restored.Value.ExchangeStatus);
        Assert.Equal("UNUSED", (await fixture.Db.Set<ETicket>().SingleAsync(i => i.OrderItemId == 101)).TicketStatus);
    }

    [Fact]
    public async Task PayAsync_ExpiredPayment_RestoresApprovedExchange()
    {
        await using var fixture = await CreateFixtureAsync([105m], [125m], fee: 5m);
        var application = CreateService(fixture);
        var created = await application.CreateAsync(7, "alice", 11,
            new CreateExchangeRequest(22, [new(101, 701, 801, "lock-701")], null));
        var issuance = new TicketIssuanceService(new SequenceTicketTokenService());
        var review = new ExchangeReviewService(
            fixture.Db, fixture.TimeProvider, new OracleExchangeLockCoordinator(fixture.Db), application,
            Options.Create(new ExchangeOptions()), issuance);
        await review.ApproveAsync("admin", created.Value!.ExchangeId, new(null));
        var child = await fixture.Db.Set<Order>().SingleAsync(i => i.OrderType == "EXCHANGE");
        child.ExpireTime = RefundTestData.FixedUtcNow;
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var payment = new ExchangePaymentService(
            fixture.Db, fixture.TimeProvider, new OracleExchangeLockCoordinator(fixture.Db),
            application, review, issuance);

        var result = await payment.PayAsync(7, "alice", created.Value.ExchangeId,
            new(PaymentChannel.ALIPAY, PaymentResult.SUCCESS));

        Assert.False(result.IsSuccess);
        Assert.Equal("EXCHANGE_PAYMENT_EXPIRED", result.ErrorCode);
        var restored = await application.GetAsync(7, created.Value.ExchangeId);
        Assert.Equal(ExchangeApproveStatus.APPROVED, restored.Value!.ApproveStatus);
        Assert.Equal(ExchangeStatus.FAILED, restored.Value.ExchangeStatus);
        Assert.Equal("UNUSED", (await fixture.Db.Set<ETicket>().SingleAsync(i => i.OrderItemId == 101)).TicketStatus);
    }

    [Fact]
    public async Task ApproveAsync_WhenExpiredRecoveryFails_PropagatesRestoreConflict()
    {
        await using var fixture = await CreateFixtureAsync([105m], [125m], fee: 0m);
        var application = CreateService(fixture);
        var created = await application.CreateAsync(7, "alice", 11,
            new CreateExchangeRequest(22, [new(101, 701, 801, "lock-701")], null));
        var child = await fixture.Db.Set<Order>().SingleAsync(i => i.OrderType == "EXCHANGE");
        child.ExpireTime = RefundTestData.FixedUtcNow;
        var targetReservation = await fixture.Db.Set<SeatReservation>()
            .SingleAsync(item => item.OrderItemId == created.Value!.Items[0].NewOrderItemId);
        targetReservation.ReservationStatus = "CANCELLED";
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var review = new ExchangeReviewService(
            fixture.Db, fixture.TimeProvider, new OracleExchangeLockCoordinator(fixture.Db), application,
            Options.Create(new ExchangeOptions()), new TicketIssuanceService(new SequenceTicketTokenService()));

        var result = await review.ApproveAsync("admin", created.Value!.ExchangeId, new(null));

        Assert.False(result.IsSuccess);
        Assert.Equal("EXCHANGE_RESTORE_CONFLICT", result.ErrorCode);
    }

    [Fact]
    public async Task PayAsync_WhenExpiredRecoveryFails_PropagatesRestoreConflict()
    {
        await using var fixture = await CreateFixtureAsync([105m], [125m], fee: 5m);
        var application = CreateService(fixture);
        var created = await application.CreateAsync(7, "alice", 11,
            new CreateExchangeRequest(22, [new(101, 701, 801, "lock-701")], null));
        var issuance = new TicketIssuanceService(new SequenceTicketTokenService());
        var review = new ExchangeReviewService(
            fixture.Db, fixture.TimeProvider, new OracleExchangeLockCoordinator(fixture.Db), application,
            Options.Create(new ExchangeOptions()), issuance);
        await review.ApproveAsync("admin", created.Value!.ExchangeId, new(null));
        var child = await fixture.Db.Set<Order>().SingleAsync(i => i.OrderType == "EXCHANGE");
        child.ExpireTime = RefundTestData.FixedUtcNow;
        var targetReservation = await fixture.Db.Set<SeatReservation>()
            .SingleAsync(item => item.OrderItemId == created.Value.Items[0].NewOrderItemId);
        targetReservation.ReservationStatus = "CANCELLED";
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var payment = new ExchangePaymentService(
            fixture.Db, fixture.TimeProvider, new OracleExchangeLockCoordinator(fixture.Db),
            application, review, issuance);

        var result = await payment.PayAsync(7, "alice", created.Value.ExchangeId,
            new(PaymentChannel.ALIPAY, PaymentResult.SUCCESS));

        Assert.False(result.IsSuccess);
        Assert.Equal("EXCHANGE_RESTORE_CONFLICT", result.ErrorCode);
    }

    [Fact]
    public async Task PayAsync_CompletedAggregateMismatch_RefusesIdempotentSuccess()
    {
        await using var fixture = await CreateFixtureAsync([105m], [125m], fee: 5m);
        var application = CreateService(fixture);
        var created = await application.CreateAsync(7, "alice", 11,
            new CreateExchangeRequest(22, [new(101, 701, 801, "lock-701")], null));
        var issuance = new TicketIssuanceService(new SequenceTicketTokenService());
        var review = new ExchangeReviewService(
            fixture.Db, fixture.TimeProvider, new OracleExchangeLockCoordinator(fixture.Db), application,
            Options.Create(new ExchangeOptions()), issuance);
        await review.ApproveAsync("admin", created.Value!.ExchangeId, new(null));
        var payment = new ExchangePaymentService(
            fixture.Db, fixture.TimeProvider, new OracleExchangeLockCoordinator(fixture.Db),
            application, review, issuance);
        var first = await payment.PayAsync(7, "alice", created.Value.ExchangeId,
            new(PaymentChannel.ALIPAY, PaymentResult.SUCCESS));
        Assert.True(first.IsSuccess);
        var child = await fixture.Db.Set<Order>()
            .SingleAsync(item => item.OrderId == first.Value!.Exchange.ChildOrderId);
        child.OrderStatus = "PENDING_PAY";
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var repeated = await payment.PayAsync(7, "alice", created.Value.ExchangeId,
            new(PaymentChannel.ALIPAY, PaymentResult.SUCCESS));

        Assert.False(repeated.IsSuccess);
        Assert.Equal("EXCHANGE_PAYMENT_CONFLICT", repeated.ErrorCode);
        Assert.Single(await fixture.Db.Set<Payment>()
            .Where(item => item.OrderId == child.OrderId && item.PayStatus == "SUCCESS")
            .ToListAsync());
    }

    private static ExchangeApplicationService CreateService(RefundTestData fixture) =>
        new(fixture.Db, new ExchangePolicyEngine(), fixture.TimeProvider);

    internal static async Task<RefundTestData> CreateFixtureAsync(
        IReadOnlyList<decimal> originalPrices,
        IReadOnlyList<decimal> targetPrices,
        decimal fee)
    {
        var fixture = await RefundTestData.CreateIssuedOrderAsync(itemPrices: originalPrices,
            totalAmount: originalPrices.Sum());
        fixture.Db.Add(new ShowSession
        {
            SessionId = 22,
            ShowId = 90,
            SeatMapId = 30,
            StartTime = RefundTestData.FixedUtcNow.AddDays(4),
            EndTime = RefundTestData.FixedUtcNow.AddDays(4).AddHours(2),
            SaleStartTime = RefundTestData.FixedUtcNow.AddDays(-1),
            SaleEndTime = RefundTestData.FixedUtcNow.AddDays(3),
            SessionStatus = "ONSALE",
        });
        fixture.Db.Add(new SeatSection
        {
            SeatSectionId = 40,
            SeatMapId = 30,
            SectionCode = "A",
            SectionName = "A",
        });
        for (var index = 0; index < targetPrices.Count; index++)
        {
            var seatId = 701L + index;
            var strategyId = 801L + index;
            fixture.Db.Add(new Seat
            {
                SeatId = seatId,
                SeatSectionId = 40,
                RowCode = "A",
                SeatNo = (index + 1).ToString(),
                RowIndex = 1,
                ColIndex = index + 1,
                SeatStatus = "ENABLED",
                IsSellable = true,
            });
            fixture.Db.Add(new PriceStrategy
            {
                PriceStrategyId = strategyId,
                SessionId = 22,
                SeatSectionId = 40,
                StrategyName = $"price-{index}",
                Price = targetPrices[index],
                SaleStartTime = RefundTestData.FixedUtcNow.AddDays(-1),
                SaleEndTime = RefundTestData.FixedUtcNow.AddDays(3),
                Status = "ENABLED",
            });
            fixture.Db.Add(new SeatLock
            {
                SeatLockId = 1001 + index,
                SessionId = 22,
                SeatId = seatId,
                UserId = 7,
                LockToken = $"lock-{seatId}",
                LockStatus = "ACTIVE",
                LockTime = RefundTestData.FixedUtcNow,
                ExpireTime = RefundTestData.FixedUtcNow.AddMinutes(10),
                CreateTime = RefundTestData.FixedUtcNow.AddDays(2),
            });
        }

        fixture.Db.Add(new ExchangePolicy
        {
            PolicyId = 401,
            ShowId = 90,
            PolicyName = "show-policy",
            ExchangeDeadlineHour = 24,
            ExchangeFee = fee,
            AllowCrossSession = 1,
            Priority = 10,
            Status = 1,
        });
        await fixture.Db.SaveChangesAsync();
        return fixture;
    }

    private sealed class SequenceTicketTokenService(params TicketCredential[] credentials) : ITicketTokenService
    {
        private int sequence;
        private readonly Queue<TicketCredential> queued = new(credentials);

        public TicketCredential Generate(DateTimeOffset issuedAt)
        {
            if (queued.Count > 0) return queued.Dequeue();
            var value = Interlocked.Increment(ref sequence);
            return new TicketCredential($"EXTKT-{value}", $"exanti-{value}", $"exqr-{value}");
        }

        public bool TryValidate(string qrCode, out TicketTokenPayload? payload)
        {
            payload = null;
            return false;
        }
    }
}
