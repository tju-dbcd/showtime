using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Common;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Entities.SeatZone;

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

    public Task<OrderTicketResult<RefundResponse>> ApproveAsync(
        string actor,
        long refundId,
        ApproveRefundRequest request,
        CancellationToken cancellationToken) => Task.FromResult(
        Internal<RefundResponse>(
            "REFUND_APPROVAL_NOT_AVAILABLE",
            "Refund approval is not available yet."));

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
            await RollbackAndClearAsync(transaction, cancellationToken);
            return Conflict<RefundResponse>(
                "REFUND_REVIEW_CONFLICT",
                "The refund request conflicted with another operation.");
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
                "Order-ticket audit sink failed for rejected refund {RefundId} on order {OrderId}.",
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
