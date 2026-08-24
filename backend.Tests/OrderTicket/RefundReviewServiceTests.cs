using ShowtimeBackend.Common;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Entities.SeatZone;
using ShowtimeBackend.Services.OrderTicket;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class RefundReviewServiceTests
{
    [Fact]
    public async Task RejectAsync_RestoresTicketAndItemButKeepsReservationActive()
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync();

        var result = await fixture.CreateReviewService().RejectAsync(
            "admin",
            fixture.RefundId,
            new RejectRefundRequest("资料不符合要求"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(RefundApproveStatus.REJECTED, result.Value!.ApproveStatus);
        Assert.Equal(RefundStatus.FAILED, result.Value.RefundStatus);
        Assert.Equal("NORMAL", await fixture.ItemStatusAsync());
        Assert.Equal("UNUSED", await fixture.TicketStatusAsync());
        Assert.Equal("ACTIVE", await fixture.ReservationStatusAsync());
        Assert.Equal(0m, await fixture.PaymentRefundAmountAsync());
        Assert.Null(await fixture.Db.Set<SeatReservation>()
            .AsNoTracking()
            .Where(item => item.OrderItemId == fixture.OrderItemIds[0])
            .Select(item => item.CancelTime)
            .SingleAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RejectAsync_RejectsBlankRemarkWithoutMutation(string remark)
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync();

        var result = await fixture.CreateReviewService().RejectAsync(
            "admin",
            fixture.RefundId,
            new RejectRefundRequest(remark),
            CancellationToken.None);

        Assert.Equal(OrderTicketFailure.InvalidRequest, result.Failure);
        Assert.Equal("REFUND_REVIEW_REMARK_INVALID", result.ErrorCode);
        Assert.Equal("REFUNDING", await fixture.ItemStatusAsync());
        Assert.Equal("REFUNDING", await fixture.TicketStatusAsync());
    }

    [Fact]
    public async Task RejectAsync_RejectsRemarkLongerThanFiveHundredCharacters()
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync();

        var result = await fixture.CreateReviewService().RejectAsync(
            "admin",
            fixture.RefundId,
            new RejectRefundRequest(new string('x', 501)),
            CancellationToken.None);

        Assert.Equal(OrderTicketFailure.InvalidRequest, result.Failure);
        Assert.Equal("REFUND_REVIEW_REMARK_INVALID", result.ErrorCode);
    }

    [Fact]
    public async Task RejectAsync_WhenAlreadyReviewed_ReturnsConflictWithoutMutation()
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync();
        await fixture.MarkRefundReviewedAsync("APPROVED", "COMPLETED");

        var result = await fixture.CreateReviewService().RejectAsync(
            "admin",
            fixture.RefundId,
            new RejectRefundRequest("拒绝"),
            CancellationToken.None);

        Assert.Equal(OrderTicketFailure.Conflict, result.Failure);
        Assert.Equal("REFUND_ALREADY_REVIEWED", result.ErrorCode);
        Assert.Equal("REFUNDING", await fixture.ItemStatusAsync());
        Assert.Equal("REFUNDING", await fixture.TicketStatusAsync());
    }

    [Fact]
    public async Task RejectAsync_WhenItemIsNotRefunding_ReturnsStableConflict()
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync();
        var item = await fixture.Db.Set<OrderItem>()
            .SingleAsync(entity => entity.OrderItemId == fixture.OrderItemIds[0]);
        item.ItemStatus = "NORMAL";
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var result = await fixture.CreateReviewService().RejectAsync(
            "admin",
            fixture.RefundId,
            new RejectRefundRequest("拒绝"),
            CancellationToken.None);

        Assert.Equal(OrderTicketFailure.Conflict, result.Failure);
        Assert.Equal("REFUND_ITEM_STATE_CONFLICT", result.ErrorCode);
        Assert.Equal("PENDING", await fixture.RefundApproveStatusAsync());
    }

    [Fact]
    public async Task RejectAsync_WhenTicketIsNotRefunding_ReturnsStableConflict()
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync();
        var ticket = await fixture.Db.Set<ETicket>()
            .SingleAsync(entity => entity.OrderItemId == fixture.OrderItemIds[0]);
        ticket.TicketStatus = "UNUSED";
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var result = await fixture.CreateReviewService().RejectAsync(
            "admin",
            fixture.RefundId,
            new RejectRefundRequest("拒绝"),
            CancellationToken.None);

        Assert.Equal(OrderTicketFailure.Conflict, result.Failure);
        Assert.Equal("REFUND_TICKET_STATE_CONFLICT", result.ErrorCode);
        Assert.Equal("PENDING", await fixture.RefundApproveStatusAsync());
    }

    [Fact]
    public async Task RejectAsync_WhenReservationIsNotActive_ReturnsStableConflict()
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync();
        var reservation = await fixture.Db.Set<SeatReservation>()
            .SingleAsync(entity => entity.OrderItemId == fixture.OrderItemIds[0]);
        reservation.ReservationStatus = "CANCELLED";
        reservation.CancelTime = RefundTestData.FixedUtcNow;
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var result = await fixture.CreateReviewService().RejectAsync(
            "admin",
            fixture.RefundId,
            new RejectRefundRequest("拒绝"),
            CancellationToken.None);

        Assert.Equal(OrderTicketFailure.Conflict, result.Failure);
        Assert.Equal("REFUND_RESERVATION_DATA_INCONSISTENT", result.ErrorCode);
        Assert.Equal("PENDING", await fixture.RefundApproveStatusAsync());
    }

    [Fact]
    public async Task RejectAsync_TrimsRemarkAndSetsTerminalReviewMetadata()
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync();

        var result = await fixture.CreateReviewService().RejectAsync(
            "review-admin",
            fixture.RefundId,
            new RejectRefundRequest("  资料不符合要求  "),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("资料不符合要求", result.Value!.ReviewRemark);
        Assert.Equal("review-admin", result.Value.ReviewBy);
        Assert.Equal(RefundTestData.FixedUtcNow, result.Value.ReviewTime);
        Assert.Equal(RefundTestData.FixedUtcNow, result.Value.CompleteTime);
        Assert.Equal("review-admin", (await fixture.Db.Set<RefundRequest>()
            .AsNoTracking()
            .SingleAsync(item => item.RefundId == fixture.RefundId)).UpdateBy);
    }

    [Fact]
    public async Task RejectAsync_BeginsTransactionAndUsesRefundThenOrderLockOrder()
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync();
        var coordinator = new RecordingLockCoordinator(fixture.Db);

        var result = await CreateService(fixture, coordinator).RejectAsync(
            "admin",
            fixture.RefundId,
            new RejectRefundRequest("拒绝"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["refund:401", "order:11"], coordinator.Calls);
        Assert.All(coordinator.TransactionObserved, Assert.True);
    }

    [Fact]
    public async Task RejectAsync_WhenRequestChangesAfterOrderLock_RevalidatesAndReturnsConflict()
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync();
        var coordinator = new CallbackAfterOrderLockCoordinator(
            fixture.Db,
            cancellationToken => fixture.Db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE REFUND_REQUEST
                SET APPROVE_STATUS = 'APPROVED', REFUND_STATUS = 'COMPLETED'
                WHERE REFUND_ID = {fixture.RefundId}
                """,
                cancellationToken));

        var result = await CreateService(fixture, coordinator).RejectAsync(
            "admin",
            fixture.RefundId,
            new RejectRefundRequest("拒绝"),
            CancellationToken.None);

        Assert.Equal(OrderTicketFailure.Conflict, result.Failure);
        Assert.Equal("REFUND_ALREADY_REVIEWED", result.ErrorCode);
        Assert.Equal(["refund:401", "order:11"], coordinator.Calls);
        Assert.True(coordinator.MutationObservedTransaction);
        Assert.Equal(1, coordinator.MutationAffectedRows);
        await AssertPendingWorkflowStateAsync(fixture);
    }

    [Fact]
    public async Task RejectAsync_WhenItemChangesAfterOrderLock_RevalidatesAssociatedState()
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync();
        var coordinator = new CallbackAfterOrderLockCoordinator(
            fixture.Db,
            cancellationToken => fixture.Db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE ORDER_ITEM
                SET ITEM_STATUS = 'EXCHANGING'
                WHERE ORDER_ITEM_ID = {fixture.OrderItemIds[0]}
                """,
                cancellationToken));

        var result = await CreateService(fixture, coordinator).RejectAsync(
            "admin",
            fixture.RefundId,
            new RejectRefundRequest("拒绝"),
            CancellationToken.None);

        Assert.Equal(OrderTicketFailure.Conflict, result.Failure);
        Assert.Equal("REFUND_ITEM_STATE_CONFLICT", result.ErrorCode);
        Assert.Equal(["refund:401", "order:11"], coordinator.Calls);
        Assert.True(coordinator.MutationObservedTransaction);
        Assert.Equal(1, coordinator.MutationAffectedRows);
        await AssertPendingWorkflowStateAsync(fixture);
    }

    [Fact]
    public async Task RejectAsync_WhenTicketChangesAfterOrderLock_RevalidatesAssociatedState()
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync();
        var coordinator = new CallbackAfterOrderLockCoordinator(
            fixture.Db,
            cancellationToken => fixture.Db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE E_TICKET
                SET TICKET_STATUS = 'USED'
                WHERE ORDER_ITEM_ID = {fixture.OrderItemIds[0]}
                """,
                cancellationToken));

        var result = await CreateService(fixture, coordinator).RejectAsync(
            "admin",
            fixture.RefundId,
            new RejectRefundRequest("拒绝"),
            CancellationToken.None);

        Assert.Equal(OrderTicketFailure.Conflict, result.Failure);
        Assert.Equal("REFUND_TICKET_STATE_CONFLICT", result.ErrorCode);
        Assert.Equal(["refund:401", "order:11"], coordinator.Calls);
        Assert.True(coordinator.MutationObservedTransaction);
        Assert.Equal(1, coordinator.MutationAffectedRows);
        await AssertPendingWorkflowStateAsync(fixture);
    }

    [Fact]
    public async Task RejectAsync_WhenReservationChangesAfterOrderLock_RevalidatesAssociatedState()
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync();
        var coordinator = new CallbackAfterOrderLockCoordinator(
            fixture.Db,
            cancellationToken => fixture.Db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE SEAT_RESERVATION
                SET RESERVATION_STATUS = 'CANCELLED'
                WHERE ORDER_ITEM_ID = {fixture.OrderItemIds[0]}
                """,
                cancellationToken));

        var result = await CreateService(fixture, coordinator).RejectAsync(
            "admin",
            fixture.RefundId,
            new RejectRefundRequest("拒绝"),
            CancellationToken.None);

        Assert.Equal(OrderTicketFailure.Conflict, result.Failure);
        Assert.Equal("REFUND_RESERVATION_DATA_INCONSISTENT", result.ErrorCode);
        Assert.Equal(["refund:401", "order:11"], coordinator.Calls);
        Assert.True(coordinator.MutationObservedTransaction);
        Assert.Equal(1, coordinator.MutationAffectedRows);
        await AssertPendingWorkflowStateAsync(fixture);
    }

    [Fact]
    public async Task RejectAsync_WhenSaveFails_RollsBackAndClearsTrackedMutations()
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync();
        await fixture.Db.Database.ExecuteSqlRawAsync(
            """
            CREATE TRIGGER FAIL_REFUND_REJECT
            BEFORE UPDATE ON REFUND_REQUEST
            BEGIN
                SELECT RAISE(ABORT, 'forced reject failure');
            END;
            """);

        var result = await fixture.CreateReviewService().RejectAsync(
            "admin",
            fixture.RefundId,
            new RejectRefundRequest("拒绝"),
            CancellationToken.None);

        Assert.Equal(OrderTicketFailure.Internal, result.Failure);
        Assert.Equal("REFUND_REJECT_FAILED", result.ErrorCode);
        Assert.Empty(fixture.Db.ChangeTracker.Entries());
        Assert.Equal("PENDING", await fixture.RefundApproveStatusAsync());
        Assert.Equal("REFUNDING", await fixture.ItemStatusAsync());
        Assert.Equal("REFUNDING", await fixture.TicketStatusAsync());
        Assert.Equal("ACTIVE", await fixture.ReservationStatusAsync());
    }

    [Fact]
    public async Task RejectAsync_WhenRequestTokensChangeAfterTracking_UsesEfConcurrencyCheck()
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync();
        var interceptor = new RawSqlConcurrencyInterceptor(
            (db, cancellationToken) => db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE REFUND_REQUEST
                SET APPROVE_STATUS = 'APPROVED', REFUND_STATUS = 'COMPLETED'
                WHERE REFUND_ID = {fixture.RefundId}
                """,
                cancellationToken));
        await using var conflictingDb = fixture.CreateDbContext(interceptor);
        var requestType = conflictingDb.Model.FindEntityType(typeof(RefundRequest))!;
        Assert.True(requestType.FindProperty(nameof(RefundRequest.ApproveStatus))!
            .IsConcurrencyToken);
        Assert.True(requestType.FindProperty(nameof(RefundRequest.RefundStatus))!
            .IsConcurrencyToken);
        var service = new RefundReviewService(
            conflictingDb,
            fixture.TimeProvider,
            new TestRefundLockCoordinator(conflictingDb),
            NullLogger<RefundReviewService>.Instance,
            new NullOrderTicketAuditSink());

        var result = await service.RejectAsync(
            "admin",
            fixture.RefundId,
            new RejectRefundRequest("拒绝"),
            CancellationToken.None);

        Assert.Equal(OrderTicketFailure.Conflict, result.Failure);
        Assert.Equal("REFUND_REVIEW_CONFLICT", result.ErrorCode);
        Assert.Equal(
            "PENDING",
            interceptor.ConcurrencyOriginalValues[
                $"{nameof(RefundRequest)}.{nameof(RefundRequest.ApproveStatus)}"]);
        Assert.Equal(
            "PENDING",
            interceptor.ConcurrencyOriginalValues[
                $"{nameof(RefundRequest)}.{nameof(RefundRequest.RefundStatus)}"]);
        Assert.True(interceptor.MutationObservedTransaction);
        Assert.Equal(1, interceptor.MutationAffectedRows);
        Assert.Equal(1, interceptor.ConcurrencyExceptionObservedCount);
        Assert.Equal(
            typeof(RefundRequest),
            Assert.Single(interceptor.ConcurrentEntityTypes));
        Assert.Empty(conflictingDb.ChangeTracker.Entries());
        await AssertPendingWorkflowStateAsync(fixture);
    }

    [Fact]
    public async Task RejectAsync_WhenItemTokenChangesAfterTracking_UsesEfConcurrencyCheck()
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync();
        var interceptor = new RawSqlConcurrencyInterceptor(
            (db, cancellationToken) => db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE ORDER_ITEM
                SET ITEM_STATUS = 'EXCHANGING'
                WHERE ORDER_ITEM_ID = {fixture.OrderItemIds[0]}
                """,
                cancellationToken));
        await using var conflictingDb = fixture.CreateDbContext(interceptor);
        var itemType = conflictingDb.Model.FindEntityType(typeof(OrderItem))!;
        Assert.True(itemType.FindProperty(nameof(OrderItem.ItemStatus))!
            .IsConcurrencyToken);
        var service = new RefundReviewService(
            conflictingDb,
            fixture.TimeProvider,
            new TestRefundLockCoordinator(conflictingDb),
            NullLogger<RefundReviewService>.Instance,
            new NullOrderTicketAuditSink());

        var result = await service.RejectAsync(
            "admin",
            fixture.RefundId,
            new RejectRefundRequest("拒绝"),
            CancellationToken.None);

        Assert.Equal(OrderTicketFailure.Conflict, result.Failure);
        Assert.Equal("REFUND_REVIEW_CONFLICT", result.ErrorCode);
        Assert.Equal(
            "REFUNDING",
            interceptor.ConcurrencyOriginalValues[
                $"{nameof(OrderItem)}.{nameof(OrderItem.ItemStatus)}"]);
        Assert.True(interceptor.MutationObservedTransaction);
        Assert.Equal(1, interceptor.MutationAffectedRows);
        Assert.Equal(1, interceptor.ConcurrencyExceptionObservedCount);
        Assert.Equal(
            typeof(OrderItem),
            Assert.Single(interceptor.ConcurrentEntityTypes));
        Assert.Empty(conflictingDb.ChangeTracker.Entries());
        await AssertPendingWorkflowStateAsync(fixture);
    }

    [Fact]
    public async Task RejectAsync_WhenTicketTokenChangesAfterTracking_UsesEfConcurrencyCheck()
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync();
        var interceptor = new RawSqlConcurrencyInterceptor(
            (db, cancellationToken) => db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE E_TICKET
                SET TICKET_STATUS = 'USED'
                WHERE ORDER_ITEM_ID = {fixture.OrderItemIds[0]}
                """,
                cancellationToken));
        await using var conflictingDb = fixture.CreateDbContext(interceptor);
        var ticketType = conflictingDb.Model.FindEntityType(typeof(ETicket))!;
        Assert.True(ticketType.FindProperty(nameof(ETicket.TicketStatus))!
            .IsConcurrencyToken);
        var service = new RefundReviewService(
            conflictingDb,
            fixture.TimeProvider,
            new TestRefundLockCoordinator(conflictingDb),
            NullLogger<RefundReviewService>.Instance,
            new NullOrderTicketAuditSink());

        var result = await service.RejectAsync(
            "admin",
            fixture.RefundId,
            new RejectRefundRequest("拒绝"),
            CancellationToken.None);

        Assert.Equal(OrderTicketFailure.Conflict, result.Failure);
        Assert.Equal("REFUND_REVIEW_CONFLICT", result.ErrorCode);
        Assert.Equal(
            "REFUNDING",
            interceptor.ConcurrencyOriginalValues[
                $"{nameof(ETicket)}.{nameof(ETicket.TicketStatus)}"]);
        Assert.True(interceptor.MutationObservedTransaction);
        Assert.Equal(1, interceptor.MutationAffectedRows);
        Assert.Equal(1, interceptor.ConcurrencyExceptionObservedCount);
        Assert.Equal(
            typeof(ETicket),
            Assert.Single(interceptor.ConcurrentEntityTypes));
        Assert.Empty(conflictingDb.ChangeTracker.Entries());
        await AssertPendingWorkflowStateAsync(fixture);
    }

    [Fact]
    public async Task RejectAsync_WhenAuditFails_KeepsCommittedRejection()
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync();
        var service = CreateService(
            fixture,
            new TestRefundLockCoordinator(fixture.Db),
            new ThrowingAuditSink());

        var result = await service.RejectAsync(
            "admin",
            fixture.RefundId,
            new RejectRefundRequest("拒绝"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("REJECTED", await fixture.RefundApproveStatusAsync());
        Assert.Equal("NORMAL", await fixture.ItemStatusAsync());
    }

    [Fact]
    public async Task RejectAsync_AuditsOnlyAfterCommitWithRejectedSnapshot()
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync(itemCount: 2);
        var auditSink = new RecordingAuditSink(fixture.Db);
        var service = CreateService(
            fixture,
            new TestRefundLockCoordinator(fixture.Db),
            auditSink);

        var result = await service.RejectAsync(
            "admin",
            fixture.RefundId,
            new RejectRefundRequest("拒绝"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var auditEvent = Assert.Single(auditSink.Events);
        Assert.Equal("REFUND_REJECTED", auditEvent.Operation);
        Assert.Equal(fixture.RefundId, auditEvent.RefundId);
        Assert.Equal(fixture.OrderId, auditEvent.OrderId);
        Assert.Equal(2, auditEvent.TicketCount);
        Assert.True(auditSink.ObservedWithoutTransaction);
        Assert.Equal("REJECTED", auditSink.ObservedApproveStatus);
    }

    [Fact]
    public async Task ListAsync_AppliesAdminFiltersAndStableDescendingPaging()
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync();
        fixture.Db.AddRange(
            Refund(402, fixture.OrderId, fixture.UserId, "PENDING", "PENDING"),
            Refund(403, fixture.OrderId, fixture.UserId, "APPROVED", "COMPLETED"),
            Refund(404, fixture.OrderId, fixture.UserId + 1, "PENDING", "PENDING"),
            Refund(405, fixture.OrderId + 1, fixture.UserId, "PENDING", "PENDING"));
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var firstPage = await fixture.CreateReviewService().ListAsync(
            new AdminRefundListQuery(null, null, null, null, null, 1, 2),
            CancellationToken.None);
        var secondPage = await fixture.CreateReviewService().ListAsync(
            new AdminRefundListQuery(null, null, null, null, null, 2, 2),
            CancellationToken.None);
        var filtered = await fixture.CreateReviewService().ListAsync(
            new AdminRefundListQuery(
                RefundApproveStatus.PENDING,
                RefundStatus.PENDING,
                fixture.OrderId,
                fixture.UserId,
                "  REF000402  "),
            CancellationToken.None);

        Assert.True(firstPage.IsSuccess);
        Assert.Equal(5, firstPage.Value!.TotalCount);
        Assert.Equal([405L, 404L], firstPage.Value.Items.Select(item => item.RefundId));
        Assert.True(secondPage.IsSuccess);
        Assert.Equal([403L, 402L], secondPage.Value!.Items.Select(item => item.RefundId));
        Assert.True(filtered.IsSuccess);
        Assert.Equal(1, filtered.Value!.TotalCount);
        Assert.Equal(402, Assert.Single(filtered.Value.Items).RefundId);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    [InlineData(int.MaxValue, 100)]
    public async Task ListAsync_WhenPagingIsInvalid_ReturnsInvalidRequest(
        int page,
        int pageSize)
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync();

        var result = await fixture.CreateReviewService().ListAsync(
            new AdminRefundListQuery(null, null, null, null, null, page, pageSize),
            CancellationToken.None);

        Assert.Equal(OrderTicketFailure.InvalidRequest, result.Failure);
        Assert.Equal("REFUND_INVALID_PAGING", result.ErrorCode);
    }

    [Fact]
    public async Task GetAsync_WhenPolicyRelationIsBroken_ReturnsMappedDetailWithNullPolicyName()
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync(itemCount: 2);
        var refund = await fixture.Db.Set<RefundRequest>()
            .SingleAsync(item => item.RefundId == fixture.RefundId);
        refund.AppliedPolicyId = 999;
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var result = await fixture.CreateReviewService().GetAsync(
            fixture.RefundId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.PolicyName);
        Assert.Equal(fixture.OrderItemIds, result.Value.Items.Select(item => item.OrderItemId));
    }

    [Fact]
    public async Task GetAsync_WhenRefundDoesNotExist_ReturnsNotFound()
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync();

        var result = await fixture.CreateReviewService().GetAsync(
            999,
            CancellationToken.None);

        Assert.Equal(OrderTicketFailure.NotFound, result.Failure);
        Assert.Equal("REFUND_NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public async Task ApproveAsync_TaskSevenPlaceholderReturnsExplicitFailureWithoutMutation()
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync();

        var result = await fixture.CreateReviewService().ApproveAsync(
            "admin",
            fixture.RefundId,
            new ApproveRefundRequest("通过"),
            CancellationToken.None);

        Assert.Equal(OrderTicketFailure.Internal, result.Failure);
        Assert.Equal("REFUND_APPROVAL_NOT_AVAILABLE", result.ErrorCode);
        Assert.Equal("PENDING", await fixture.RefundApproveStatusAsync());
        Assert.Equal("REFUNDING", await fixture.ItemStatusAsync());
        Assert.Equal(0m, await fixture.PaymentRefundAmountAsync());
        Assert.Equal("ACTIVE", await fixture.ReservationStatusAsync());
    }

    private static RefundReviewService CreateService(
        RefundTestData fixture,
        IRefundLockCoordinator coordinator,
        IOrderTicketAuditSink? auditSink = null) => new(
        fixture.Db,
        fixture.TimeProvider,
        coordinator,
        NullLogger<RefundReviewService>.Instance,
        auditSink ?? new NullOrderTicketAuditSink());

    private static async Task AssertPendingWorkflowStateAsync(RefundTestData fixture)
    {
        var requestState = await fixture.Db.Set<RefundRequest>()
            .AsNoTracking()
            .Where(item => item.RefundId == fixture.RefundId)
            .Select(item => new { item.ApproveStatus, item.RefundStatus })
            .SingleAsync();
        Assert.Equal("PENDING", requestState.ApproveStatus);
        Assert.Equal("PENDING", requestState.RefundStatus);
        Assert.Equal("REFUNDING", await fixture.ItemStatusAsync());
        Assert.Equal("REFUNDING", await fixture.TicketStatusAsync());
        Assert.Equal("ACTIVE", await fixture.ReservationStatusAsync());
        Assert.Equal(0m, await fixture.PaymentRefundAmountAsync());
    }

    private static RefundRequest Refund(
        long refundId,
        long orderId,
        long userId,
        string approveStatus,
        string refundStatus) => new()
    {
        RefundId = refundId,
        RefundNo = $"REF{refundId:000000}",
        OrderId = orderId,
        UserId = userId,
        RefundType = "PART",
        RefundReason = "测试申请",
        RefundAmount = 10m,
        ActualRefund = 8m,
        FeeRate = 0.8m,
        AppliedServiceFee = 0m,
        ApproveStatus = approveStatus,
        RefundStatus = refundStatus,
        CompleteTime = refundStatus == "COMPLETED" ? RefundTestData.FixedUtcNow : null,
        CreateTime = RefundTestData.FixedUtcNow,
    };

    private sealed class RecordingLockCoordinator(AppDbContext db)
        : IRefundLockCoordinator
    {
        public List<string> Calls { get; } = [];
        public List<bool> TransactionObserved { get; } = [];

        public async Task<bool> LockRefundRequestAsync(
            long refundId,
            CancellationToken cancellationToken)
        {
            Calls.Add($"refund:{refundId}");
            TransactionObserved.Add(db.Database.CurrentTransaction is not null);
            return await db.Set<RefundRequest>()
                .AsNoTracking()
                .AnyAsync(item => item.RefundId == refundId, cancellationToken);
        }

        public async Task<bool> LockOrderAsync(
            long orderId,
            CancellationToken cancellationToken)
        {
            Calls.Add($"order:{orderId}");
            TransactionObserved.Add(db.Database.CurrentTransaction is not null);
            return await db.Set<Order>()
                .AsNoTracking()
                .AnyAsync(item => item.OrderId == orderId, cancellationToken);
        }
    }

    private sealed class RecordingAuditSink(AppDbContext db) : IOrderTicketAuditSink
    {
        public List<OrderTicketAuditEvent> Events { get; } = [];
        public bool ObservedWithoutTransaction { get; private set; }
        public string? ObservedApproveStatus { get; private set; }

        public async ValueTask WriteAsync(
            OrderTicketAuditEvent auditEvent,
            CancellationToken cancellationToken)
        {
            Events.Add(auditEvent);
            ObservedWithoutTransaction = db.Database.CurrentTransaction is null;
            ObservedApproveStatus = await db.Set<RefundRequest>()
                .AsNoTracking()
                .Where(item => item.RefundId == auditEvent.RefundId)
                .Select(item => item.ApproveStatus)
                .SingleAsync(cancellationToken);
        }
    }

    private sealed class CallbackAfterOrderLockCoordinator(
        AppDbContext db,
        Func<CancellationToken, Task<int>> mutateAsync)
        : IRefundLockCoordinator
    {
        public List<string> Calls { get; } = [];
        public bool MutationObservedTransaction { get; private set; }
        public int MutationAffectedRows { get; private set; }

        public async Task<bool> LockRefundRequestAsync(
            long refundId,
            CancellationToken cancellationToken)
        {
            Calls.Add($"refund:{refundId}");
            return await db.Set<RefundRequest>()
                .AsNoTracking()
                .AnyAsync(item => item.RefundId == refundId, cancellationToken);
        }

        public async Task<bool> LockOrderAsync(
            long orderId,
            CancellationToken cancellationToken)
        {
            Calls.Add($"order:{orderId}");
            var exists = await db.Set<Order>()
                .AsNoTracking()
                .AnyAsync(item => item.OrderId == orderId, cancellationToken);
            MutationObservedTransaction = db.Database.CurrentTransaction is not null;
            MutationAffectedRows += await mutateAsync(cancellationToken);
            return exists;
        }
    }

    private sealed class ThrowingAuditSink : IOrderTicketAuditSink
    {
        public ValueTask WriteAsync(
            OrderTicketAuditEvent auditEvent,
            CancellationToken cancellationToken) => ValueTask.FromException(
                new InvalidOperationException("audit unavailable"));
    }

    private sealed class RawSqlConcurrencyInterceptor(
        Func<AppDbContext, CancellationToken, Task<int>> mutateAsync)
        : SaveChangesInterceptor
    {
        public int MutationAffectedRows { get; private set; }
        public int ConcurrencyExceptionObservedCount { get; private set; }
        public bool MutationObservedTransaction { get; private set; }
        public IReadOnlyList<Type> ConcurrentEntityTypes { get; private set; } = [];
        public IReadOnlyDictionary<string, object?> ConcurrencyOriginalValues
            { get; private set; } = new Dictionary<string, object?>();

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var db = (AppDbContext)eventData.Context!;
            ConcurrencyOriginalValues = db.ChangeTracker.Entries()
                .SelectMany(entry => entry.Properties
                    .Where(property => property.Metadata.IsConcurrencyToken)
                    .Select(property => new KeyValuePair<string, object?>(
                        $"{entry.Metadata.ClrType.Name}.{property.Metadata.Name}",
                        property.OriginalValue)))
                .ToDictionary();
            MutationObservedTransaction = db.Database.CurrentTransaction is not null;
            MutationAffectedRows += await mutateAsync(db, cancellationToken);
            return result;
        }

        public override ValueTask<InterceptionResult> ThrowingConcurrencyExceptionAsync(
            ConcurrencyExceptionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            ConcurrencyExceptionObservedCount++;
            ConcurrentEntityTypes = eventData.Exception.Entries
                .Select(entry => entry.Metadata.ClrType)
                .ToList();
            return ValueTask.FromResult(result);
        }
    }
}
