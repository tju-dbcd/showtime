using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Common;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Entities.SeatZone;
using ShowtimeBackend.Services.OrderTicket.Messaging;

namespace ShowtimeBackend.Services.OrderTicket;

public sealed class RefundReviewService(
    AppDbContext dbContext,
    TimeProvider timeProvider,
    IRefundLockCoordinator lockCoordinator,
    ILogger<RefundReviewService> logger,
    IOrderTicketAuditSink auditSink) : IRefundReviewService
{
    public async Task<OrderTicketResult<PagedRefundResponse>> ListAsync(
        AdminRefundListQuery query,
        CancellationToken cancellationToken)
    {
        var offset = ((long)query.Page - 1) * query.PageSize;
        if (query.Page < 1 || query.PageSize is < 1 or > 100 || offset > int.MaxValue)
        {
            return Invalid<PagedRefundResponse>(
                "REFUND_INVALID_PAGING",
                "Page must be positive and pageSize must be between 1 and 100.");
        }

        var refunds = dbContext.Set<RefundRequest>()
            .AsNoTracking()
            .AsQueryable();
        if (query.ApproveStatus.HasValue)
        {
            var approveStatus = query.ApproveStatus.Value.ToDbString();
            refunds = refunds.Where(item => item.ApproveStatus == approveStatus);
        }

        if (query.RefundStatus.HasValue)
        {
            var refundStatus = query.RefundStatus.Value.ToDbString();
            refunds = refunds.Where(item => item.RefundStatus == refundStatus);
        }

        if (query.OrderId.HasValue)
        {
            refunds = refunds.Where(item => item.OrderId == query.OrderId.Value);
        }

        if (query.UserId.HasValue)
        {
            refunds = refunds.Where(item => item.UserId == query.UserId.Value);
        }

        var refundNo = query.RefundNo?.Trim();
        if (!string.IsNullOrEmpty(refundNo))
        {
            refunds = refunds.Where(item => item.RefundNo == refundNo);
        }

        var totalCount = await refunds.CountAsync(cancellationToken);
        var items = await refunds
            .OrderByDescending(item => item.CreateTime)
            .ThenByDescending(item => item.RefundId)
            .Skip((int)offset)
            .Take(query.PageSize)
            .Select(item => new RefundSummaryResponse(
                item.RefundId,
                item.RefundNo,
                item.OrderId,
                item.RefundType.ToEnum<RefundType>(),
                item.ActualRefund,
                item.ApproveStatus.ToEnum<RefundApproveStatus>(),
                item.RefundStatus.ToEnum<RefundStatus>(),
                item.CreateTime,
                item.CompleteTime))
            .ToListAsync(cancellationToken);

        return OrderTicketResult<PagedRefundResponse>.Success(
            new PagedRefundResponse(items, query.Page, query.PageSize, totalCount));
    }

    public async Task<OrderTicketResult<RefundResponse>> GetAsync(
        long refundId,
        CancellationToken cancellationToken)
    {
        var refundRequest = await dbContext.Set<RefundRequest>()
            .AsNoTracking()
            .Include(item => item.AppliedPolicy)
            .Include(item => item.Items)
                .ThenInclude(item => item.OrderItem)
                    .ThenInclude(item => item!.ETicket)
            .SingleOrDefaultAsync(item => item.RefundId == refundId, cancellationToken);
        if (refundRequest is null)
        {
            return NotFound<RefundResponse>(
                "REFUND_NOT_FOUND",
                "The refund request does not exist.");
        }

        return OrderTicketResult<RefundResponse>.Success(
            RefundResponseMapper.ToResponse(
                refundRequest,
                refundRequest.AppliedPolicy?.PolicyName));
    }

    public async Task<OrderTicketResult<RefundResponse>> ApproveAsync(
        string actor,
        long refundId,
        ApproveRefundRequest request,
        CancellationToken cancellationToken)
    {
        var remark = request?.Remark?.Trim();
        if (remark?.Length > 500)
        {
            return Invalid<RefundResponse>(
                "REFUND_REVIEW_REMARK_INVALID",
                "Approve remark must not exceed 500 characters.");
        }

        if (string.IsNullOrEmpty(remark))
        {
            remark = null;
        }

        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(cancellationToken);
        try
        {
            if (!await lockCoordinator.LockRefundRequestAsync(
                    refundId,
                    cancellationToken))
            {
                await RollbackAndClearAsync(transaction, cancellationToken);
                return NotFound<RefundResponse>(
                    "REFUND_NOT_FOUND",
                    "The refund request does not exist.");
            }

            var lockedRequest = await dbContext.Set<RefundRequest>()
                .AsNoTracking()
                .Where(item => item.RefundId == refundId)
                .Select(item => new
                {
                    item.OrderId,
                    item.ApproveStatus,
                    item.RefundStatus,
                })
                .SingleOrDefaultAsync(cancellationToken);
            if (lockedRequest is null)
            {
                await RollbackAndClearAsync(transaction, cancellationToken);
                return Conflict<RefundResponse>(
                    "REFUND_REVIEW_CONFLICT",
                    "The refund request changed during review.");
            }

            if (lockedRequest.ApproveStatus != "PENDING" ||
                lockedRequest.RefundStatus != "PENDING")
            {
                await RollbackAndClearAsync(transaction, cancellationToken);
                return Conflict<RefundResponse>(
                    "REFUND_ALREADY_REVIEWED",
                    "The refund request has already been reviewed.");
            }

            if (!await lockCoordinator.LockOrderAsync(
                    lockedRequest.OrderId,
                    cancellationToken))
            {
                await RollbackAndClearAsync(transaction, cancellationToken);
                return Conflict<RefundResponse>(
                    "REFUND_ORDER_DATA_INCONSISTENT",
                    "The refund order does not exist.");
            }

            dbContext.ChangeTracker.Clear();
            var refundRequest = await dbContext.Set<RefundRequest>()
                .Include(item => item.Items)
                    .ThenInclude(item => item.OrderItem)
                        .ThenInclude(item => item!.ETicket)
                .SingleOrDefaultAsync(item => item.RefundId == refundId, cancellationToken);
            if (refundRequest is null || refundRequest.OrderId != lockedRequest.OrderId)
            {
                await RollbackAndClearAsync(transaction, cancellationToken);
                return Conflict<RefundResponse>(
                    "REFUND_REVIEW_CONFLICT",
                    "The refund request changed during review.");
            }

            if (refundRequest.ApproveStatus != "PENDING" ||
                refundRequest.RefundStatus != "PENDING")
            {
                await RollbackAndClearAsync(transaction, cancellationToken);
                return Conflict<RefundResponse>(
                    "REFUND_ALREADY_REVIEWED",
                    "The refund request has already been reviewed.");
            }

            if (!refundRequest.ActualRefund.HasValue ||
                refundRequest.ActualRefund.Value <= 0m)
            {
                await RollbackAndClearAsync(transaction, cancellationToken);
                return Conflict<RefundResponse>(
                    "REFUND_AMOUNT_NOT_POSITIVE",
                    "The frozen refund amount must be positive.");
            }

            var refundItems = refundRequest.Items
                .OrderBy(item => item.OrderItemId)
                .ToList();
            if (refundItems.Count == 0 ||
                refundItems.Sum(item => item.RefundBaseAmount) !=
                    refundRequest.RefundAmount)
            {
                await RollbackAndClearAsync(transaction, cancellationToken);
                return Conflict<RefundResponse>(
                    "REFUND_PAYMENT_DATA_INCONSISTENT",
                    "The frozen refund amounts are inconsistent.");
            }

            var order = await dbContext.Set<Order>()
                .SingleOrDefaultAsync(
                    item => item.OrderId == refundRequest.OrderId,
                    cancellationToken);
            var allOrderItems = await dbContext.Set<OrderItem>()
                .Where(item => item.OrderId == refundRequest.OrderId)
                .Include(item => item.ETicket)
                .OrderBy(item => item.OrderItemId)
                .ToListAsync(cancellationToken);
            if (order is null || allOrderItems.Count == 0)
            {
                await RollbackAndClearAsync(transaction, cancellationToken);
                return Conflict<RefundResponse>(
                    "REFUND_ORDER_DATA_INCONSISTENT",
                    "The refund order data is inconsistent.");
            }

            if (refundItems.Any(item =>
                    item.OrderItem is null ||
                    item.OrderItem.OrderId != refundRequest.OrderId ||
                    item.OrderItem.ItemStatus != "REFUNDING"))
            {
                await RollbackAndClearAsync(transaction, cancellationToken);
                return Conflict<RefundResponse>(
                    "REFUND_ITEM_STATE_CONFLICT",
                    "A refund item is no longer awaiting review.");
            }

            if (refundItems.Any(item =>
                    item.OrderItem!.ETicket is null ||
                    item.OrderItem.ETicket.OrderItemId != item.OrderItemId ||
                    item.OrderItem.ETicket.TicketStatus != "REFUNDING"))
            {
                await RollbackAndClearAsync(transaction, cancellationToken);
                return Conflict<RefundResponse>(
                    "REFUND_TICKET_STATE_CONFLICT",
                    "A refund ticket is no longer awaiting review.");
            }

            var payments = await dbContext.Set<Payment>()
                .AsNoTracking()
                .Where(item => item.OrderId == order.OrderId &&
                    item.PayStatus == "SUCCESS")
                .Select(item => new
                {
                    item.PaymentId,
                    item.PayAmount,
                    item.RefundAmount,
                })
                .ToListAsync(cancellationToken);
            var orderItemTotal = allOrderItems.Sum(item => item.UnitPrice);
            if (payments.Count != 1 ||
                payments[0].PayAmount <= 0m ||
                payments[0].PayAmount != order.TotalAmount - order.DiscountAmount ||
                orderItemTotal <= 0m ||
                orderItemTotal != order.TotalAmount)
            {
                await RollbackAndClearAsync(transaction, cancellationToken);
                return Conflict<RefundResponse>(
                    "REFUND_PAYMENT_DATA_INCONSISTENT",
                    "The payment and order amounts are inconsistent.");
            }

            if (payments[0].RefundAmount + refundRequest.ActualRefund.Value >
                payments[0].PayAmount)
            {
                await RollbackAndClearAsync(transaction, cancellationToken);
                return Conflict<RefundResponse>(
                    "REFUND_PAYMENT_AMOUNT_CONFLICT",
                    "The refundable payment amount changed.");
            }

            var orderItemIds = refundItems
                .Select(item => item.OrderItemId)
                .ToArray();
            var reservationRows = await dbContext.Set<SeatReservation>()
                .AsNoTracking()
                .Where(item => item.OrderItemId.HasValue &&
                    orderItemIds.Contains(item.OrderItemId.Value))
                .Select(item => new
                {
                    item.SeatReservationId,
                    item.OrderItemId,
                    item.ReservationType,
                    item.ReservationStatus,
                })
                .ToListAsync(cancellationToken);
            var reservationsValid = reservationRows.Count == orderItemIds.Length &&
                reservationRows.All(item =>
                    item.ReservationType == "ORDER" &&
                    item.ReservationStatus == "ACTIVE") &&
                reservationRows
                    .GroupBy(item => item.OrderItemId)
                    .All(group => group.Count() == 1);
            if (!reservationsValid)
            {
                await RollbackAndClearAsync(transaction, cancellationToken);
                return Conflict<RefundResponse>(
                    "REFUND_RESERVATION_DATA_INCONSISTENT",
                    "Seat reservation data is inconsistent.");
            }

            string? policyName = null;
            if (refundRequest.AppliedPolicyId is long policyId)
            {
                policyName = await dbContext.Set<RefundPolicy>()
                    .AsNoTracking()
                    .Where(item => item.PolicyId == policyId)
                    .Select(item => item.PolicyName)
                    .SingleOrDefaultAsync(cancellationToken);
            }

            var actualRefund = refundRequest.ActualRefund.Value;
            var now = timeProvider.GetUtcNow().UtcDateTime;
            refundRequest.ApproveStatus = "APPROVED";
            refundRequest.RefundStatus = "PROCESSING";
            refundRequest.ReviewBy = actor;
            refundRequest.ReviewTime = now;
            refundRequest.ReviewRemark = remark;
            refundRequest.CompleteTime = null;
            refundRequest.UpdateBy = actor;

            var eventId = Guid.NewGuid().ToString("D");
            var approvedEvent = new RefundApprovedEvent(
                eventId,
                RefundApprovedEvent.TypeName,
                DateTime.SpecifyKind(now, DateTimeKind.Utc),
                refundRequest.RefundId,
                refundRequest.RefundNo,
                refundRequest.OrderId,
                refundRequest.UserId,
                actualRefund);
            dbContext.OrderEventOutbox.Add(new OrderEventOutbox
            {
                EventId = eventId,
                EventType = RefundApprovedEvent.TypeName,
                RoutingKey = RefundApprovedEvent.RoutingKeyName,
                AggregateId = refundRequest.RefundId,
                UserId = refundRequest.UserId,
                Payload = approvedEvent.Serialize(),
                OccurredAt = now,
                Status = "PENDING",
                NextAttemptAt = now,
                CreateBy = actor,
                UpdateBy = actor,
            });

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var response = RefundResponseMapper.ToResponse(refundRequest, policyName);
            await WriteAuditSafelyAsync(
                new OrderTicketAuditEvent(
                    "REFUND_APPROVED",
                    refundRequest.OrderId,
                    actor,
                    refundItems.Count,
                    now,
                    refundRequest.RefundId,
                    actualRefund,
                    new Dictionary<string, string>
                    {
                        ["ApproveStatus"] = refundRequest.ApproveStatus,
                        ["RefundStatus"] = refundRequest.RefundStatus,
                    }),
                cancellationToken);

            return OrderTicketResult<RefundResponse>.Success(response);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await RollbackAndClearAsync(transaction, CancellationToken.None);
            throw;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            logger.LogWarning(
                exception,
                "Concurrent refund approval detected for refund {RefundId}.",
                refundId);
            return await RecoverReviewConflictAsync(
                transaction,
                refundId,
                "REFUND_REVIEW_CONFLICT",
                "The refund request conflicted with another operation.",
                cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Refund approval failed for refund {RefundId}.",
                refundId);
            await RollbackAndClearAsync(transaction, cancellationToken);
            return Internal<RefundResponse>(
                "REFUND_APPROVE_FAILED",
                "The refund request could not be approved.");
        }
    }

    public async Task<OrderTicketResult<RefundResponse>> RejectAsync(
        string actor,
        long refundId,
        RejectRefundRequest request,
        CancellationToken cancellationToken)
    {
        var remark = request?.Remark?.Trim();
        if (string.IsNullOrEmpty(remark) || remark.Length > 500)
        {
            return Invalid<RefundResponse>(
                "REFUND_REVIEW_REMARK_INVALID",
                "Reject remark is required and must not exceed 500 characters.");
        }

        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(cancellationToken);
        if (!await lockCoordinator.LockRefundRequestAsync(refundId, cancellationToken))
        {
            await RollbackAndClearAsync(transaction, cancellationToken);
            return NotFound<RefundResponse>(
                "REFUND_NOT_FOUND",
                "The refund request does not exist.");
        }

        var lockedRequest = await dbContext.Set<RefundRequest>()
            .AsNoTracking()
            .Where(item => item.RefundId == refundId)
            .Select(item => new
            {
                item.OrderId,
                item.ApproveStatus,
                item.RefundStatus,
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (lockedRequest is null)
        {
            await RollbackAndClearAsync(transaction, cancellationToken);
            return Conflict<RefundResponse>(
                "REFUND_REVIEW_CONFLICT",
                "The refund request changed during review.");
        }

        if (lockedRequest.ApproveStatus != "PENDING" ||
            lockedRequest.RefundStatus != "PENDING")
        {
            await RollbackAndClearAsync(transaction, cancellationToken);
            return Conflict<RefundResponse>(
                "REFUND_ALREADY_REVIEWED",
                "The refund request has already been reviewed.");
        }

        if (!await lockCoordinator.LockOrderAsync(
                lockedRequest.OrderId,
                cancellationToken))
        {
            await RollbackAndClearAsync(transaction, cancellationToken);
            return Conflict<RefundResponse>(
                "REFUND_ORDER_DATA_INCONSISTENT",
                "The refund order does not exist.");
        }

        dbContext.ChangeTracker.Clear();
        var refundRequest = await dbContext.Set<RefundRequest>()
            .Include(item => item.AppliedPolicy)
            .SingleOrDefaultAsync(item => item.RefundId == refundId, cancellationToken);
        if (refundRequest is null || refundRequest.OrderId != lockedRequest.OrderId)
        {
            await RollbackAndClearAsync(transaction, cancellationToken);
            return Conflict<RefundResponse>(
                "REFUND_REVIEW_CONFLICT",
                "The refund request changed during review.");
        }

        if (refundRequest.ApproveStatus != "PENDING" ||
            refundRequest.RefundStatus != "PENDING")
        {
            await RollbackAndClearAsync(transaction, cancellationToken);
            return Conflict<RefundResponse>(
                "REFUND_ALREADY_REVIEWED",
                "The refund request has already been reviewed.");
        }

        var refundItems = await dbContext.Set<RefundItem>()
            .Where(item => item.RefundId == refundId)
            .Include(item => item.OrderItem)
                .ThenInclude(item => item!.ETicket)
            .OrderBy(item => item.OrderItemId)
            .ToListAsync(cancellationToken);
        if (refundItems.Count == 0 || refundItems.Any(item =>
                item.OrderItem is null ||
                item.OrderItem.OrderId != refundRequest.OrderId ||
                item.OrderItem.ItemStatus != "REFUNDING"))
        {
            await RollbackAndClearAsync(transaction, cancellationToken);
            return Conflict<RefundResponse>(
                "REFUND_ITEM_STATE_CONFLICT",
                "A refund item is no longer awaiting review.");
        }

        if (refundItems.Any(item =>
                item.OrderItem!.ETicket is null ||
                item.OrderItem.ETicket.OrderItemId != item.OrderItemId ||
                item.OrderItem.ETicket.TicketStatus != "REFUNDING"))
        {
            await RollbackAndClearAsync(transaction, cancellationToken);
            return Conflict<RefundResponse>(
                "REFUND_TICKET_STATE_CONFLICT",
                "A refund ticket is no longer awaiting review.");
        }

        var orderItemIds = refundItems
            .Select(item => item.OrderItemId)
            .ToList();
        var reservationRows = await dbContext.Set<SeatReservation>()
            .AsNoTracking()
            .Where(item => item.OrderItemId.HasValue &&
                orderItemIds.Contains(item.OrderItemId.Value))
            .Select(item => new
            {
                item.OrderItemId,
                item.ReservationType,
                item.ReservationStatus,
            })
            .ToListAsync(cancellationToken);
        if (orderItemIds.Any(orderItemId => reservationRows.Count(item =>
                item.OrderItemId == orderItemId &&
                item.ReservationType == "ORDER" &&
                item.ReservationStatus == "ACTIVE") != 1))
        {
            await RollbackAndClearAsync(transaction, cancellationToken);
            return Conflict<RefundResponse>(
                "REFUND_RESERVATION_DATA_INCONSISTENT",
                "Each refund item must keep one active order reservation.");
        }

        var reviewedAt = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var refundItem in refundItems)
        {
            refundItem.OrderItem!.ItemStatus = "NORMAL";
            refundItem.OrderItem.UpdateBy = actor;
            refundItem.OrderItem.ETicket!.TicketStatus = "UNUSED";
            refundItem.OrderItem.ETicket.UpdateBy = actor;
        }

        refundRequest.ApproveStatus = "REJECTED";
        refundRequest.RefundStatus = "FAILED";
        refundRequest.ReviewBy = actor;
        refundRequest.ReviewTime = reviewedAt;
        refundRequest.ReviewRemark = remark;
        refundRequest.CompleteTime = reviewedAt;
        refundRequest.UpdateBy = actor;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            logger.LogWarning(
                exception,
                "Concurrent refund rejection detected for refund {RefundId}.",
                refundId);
            return await RecoverReviewConflictAsync(
                transaction,
                refundId,
                "REFUND_REVIEW_CONFLICT",
                "The refund request conflicted with another operation.",
                cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            logger.LogError(
                exception,
                "Refund rejection persistence failed for refund {RefundId}.",
                refundId);
            await RollbackAndClearAsync(transaction, cancellationToken);
            return Internal<RefundResponse>(
                "REFUND_REJECT_FAILED",
                "The refund request could not be rejected.");
        }

        var response = RefundResponseMapper.ToResponse(
            refundRequest,
            refundRequest.AppliedPolicy?.PolicyName);
        await WriteAuditSafelyAsync(
            new OrderTicketAuditEvent(
                "REFUND_REJECTED",
                refundRequest.OrderId,
                actor,
                refundItems.Count,
                reviewedAt,
                refundRequest.RefundId,
                refundRequest.ActualRefund,
                new Dictionary<string, string>
                {
                    ["ApproveStatus"] = refundRequest.ApproveStatus,
                    ["RefundStatus"] = refundRequest.RefundStatus,
                }),
            cancellationToken);

        return OrderTicketResult<RefundResponse>.Success(response);
    }

    private async Task RollbackAndClearAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
    }

    private async Task<OrderTicketResult<RefundResponse>> RecoverReviewConflictAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        long refundId,
        string fallbackCode,
        string fallbackMessage,
        CancellationToken cancellationToken)
    {
        await RollbackAndClearAsync(transaction, CancellationToken.None);
        try
        {
            var latest = await dbContext.Set<RefundRequest>()
                .AsNoTracking()
                .Where(item => item.RefundId == refundId)
                .Select(item => new
                {
                    item.ApproveStatus,
                    item.RefundStatus,
                })
                .SingleOrDefaultAsync(cancellationToken);
            if (latest is not null &&
                (latest.ApproveStatus != "PENDING" || latest.RefundStatus != "PENDING"))
            {
                return Conflict<RefundResponse>(
                    "REFUND_ALREADY_REVIEWED",
                    "The refund request has already been reviewed.");
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Refund review conflict recovery read failed for refund {RefundId}.",
                refundId);
        }

        return Conflict<RefundResponse>(
            fallbackCode,
            fallbackMessage);
    }

    private async ValueTask WriteAuditSafelyAsync(
        OrderTicketAuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        try
        {
            await auditSink.WriteAsync(auditEvent, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Order-ticket audit sink failed for refund {RefundId} on order {OrderId}.",
                auditEvent.RefundId,
                auditEvent.OrderId);
        }
    }

    private static OrderTicketResult<T> Invalid<T>(string code, string message) =>
        OrderTicketResult<T>.Fail(OrderTicketFailure.InvalidRequest, code, message);

    private static OrderTicketResult<T> NotFound<T>(string code, string message) =>
        OrderTicketResult<T>.Fail(OrderTicketFailure.NotFound, code, message);

    private static OrderTicketResult<T> Conflict<T>(string code, string message) =>
        OrderTicketResult<T>.Fail(OrderTicketFailure.Conflict, code, message);

    private static OrderTicketResult<T> Internal<T>(string code, string message) =>
        OrderTicketResult<T>.Fail(OrderTicketFailure.Internal, code, message);
}
