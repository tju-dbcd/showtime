using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ShowtimeBackend.Data;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Entities.SeatZone;

namespace ShowtimeBackend.Services.OrderTicket.Messaging;

public enum RefundCompletionOutcome
{
    Completed,
    AlreadyCompleted,
    PermanentFailure,
    RetryableFailure,
}

public sealed record RefundCompletionResult(
    RefundCompletionOutcome Outcome,
    string? Code = null,
    string? Message = null)
{
    public static RefundCompletionResult Completed() => new(RefundCompletionOutcome.Completed);
    public static RefundCompletionResult AlreadyCompleted() => new(RefundCompletionOutcome.AlreadyCompleted);
    public static RefundCompletionResult Permanent(string code, string message) =>
        new(RefundCompletionOutcome.PermanentFailure, code, message);
    public static RefundCompletionResult Retryable(string code, string message) =>
        new(RefundCompletionOutcome.RetryableFailure, code, message);
}

public interface IRefundCompletionService
{
    Task<RefundCompletionResult> CompleteAsync(
        RefundApprovedEvent approvedEvent,
        CancellationToken cancellationToken);
}

public sealed class RefundCompletionService(
    AppDbContext dbContext,
    TimeProvider timeProvider,
    IRefundLockCoordinator lockCoordinator,
    ILogger<RefundCompletionService> logger,
    IOrderTicketAuditSink auditSink) : IRefundCompletionService
{
    public const string SystemActor = "rabbitmq-refund-worker";

    public async Task<RefundCompletionResult> CompleteAsync(
        RefundApprovedEvent approvedEvent,
        CancellationToken cancellationToken)
    {
        if (!IsStructurallyValid(approvedEvent))
        {
            return RefundCompletionResult.Permanent(
                "REFUND_EVENT_INVALID",
                "The refund approval event is invalid.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (!await lockCoordinator.LockRefundRequestAsync(
                    approvedEvent.RefundId,
                    cancellationToken))
            {
                await RollbackAndClearAsync(transaction, cancellationToken);
                return RefundCompletionResult.Permanent(
                    "REFUND_NOT_FOUND",
                    "The refund request does not exist.");
            }

            var lockedRequest = await dbContext.Set<RefundRequest>()
                .AsNoTracking()
                .Where(item => item.RefundId == approvedEvent.RefundId)
                .Select(item => new
                {
                    item.OrderId,
                    item.UserId,
                    item.RefundNo,
                    item.ActualRefund,
                    item.ApproveStatus,
                    item.RefundStatus,
                })
                .SingleOrDefaultAsync(cancellationToken);
            if (lockedRequest is null)
            {
                await RollbackAndClearAsync(transaction, cancellationToken);
                return RefundCompletionResult.Retryable(
                    "REFUND_COMPLETION_CONFLICT",
                    "The refund request changed while it was being locked.");
            }

            var identityFailure = ValidatePersistentIdentity(
                approvedEvent,
                lockedRequest.OrderId,
                lockedRequest.UserId,
                lockedRequest.RefundNo,
                lockedRequest.ActualRefund);
            if (identityFailure is not null)
            {
                await RollbackAndClearAsync(transaction, cancellationToken);
                return identityFailure;
            }

            if (lockedRequest.ApproveStatus == "APPROVED" &&
                lockedRequest.RefundStatus == "COMPLETED")
            {
                await RollbackAndClearAsync(transaction, cancellationToken);
                return RefundCompletionResult.AlreadyCompleted();
            }

            if (lockedRequest.ApproveStatus != "APPROVED" ||
                lockedRequest.RefundStatus != "PROCESSING")
            {
                await RollbackAndClearAsync(transaction, cancellationToken);
                return RefundCompletionResult.Permanent(
                    "REFUND_STATE_INVALID",
                    "Only an approved, processing refund can be completed.");
            }

            if (!await lockCoordinator.LockOrderAsync(
                    lockedRequest.OrderId,
                    cancellationToken))
            {
                await RollbackAndClearAsync(transaction, cancellationToken);
                return RefundCompletionResult.Permanent(
                    "REFUND_ORDER_DATA_INCONSISTENT",
                    "The refund order does not exist.");
            }

            dbContext.ChangeTracker.Clear();
            var refundRequest = await dbContext.Set<RefundRequest>()
                .Include(item => item.Items)
                    .ThenInclude(item => item.OrderItem)
                        .ThenInclude(item => item!.ETicket)
                .SingleOrDefaultAsync(
                    item => item.RefundId == approvedEvent.RefundId,
                    cancellationToken);
            if (refundRequest is null)
            {
                return await RollbackRetryableAsync(transaction, cancellationToken);
            }

            identityFailure = ValidatePersistentIdentity(
                approvedEvent,
                refundRequest.OrderId,
                refundRequest.UserId,
                refundRequest.RefundNo,
                refundRequest.ActualRefund);
            if (identityFailure is not null)
            {
                await RollbackAndClearAsync(transaction, cancellationToken);
                return identityFailure;
            }

            if (refundRequest.ApproveStatus == "APPROVED" &&
                refundRequest.RefundStatus == "COMPLETED")
            {
                await RollbackAndClearAsync(transaction, cancellationToken);
                return RefundCompletionResult.AlreadyCompleted();
            }

            if (refundRequest.ApproveStatus != "APPROVED" ||
                refundRequest.RefundStatus != "PROCESSING")
            {
                await RollbackAndClearAsync(transaction, cancellationToken);
                return RefundCompletionResult.Permanent(
                    "REFUND_STATE_INVALID",
                    "The refund state changed before completion.");
            }

            var result = await ApplyCompletionAsync(
                refundRequest,
                transaction,
                cancellationToken);
            if (result.Outcome != RefundCompletionOutcome.Completed)
            {
                return result;
            }

            var completedAt = refundRequest.CompleteTime!.Value;
            var ticketCount = refundRequest.Items.Count;
            await WriteAuditSafelyAsync(
                new OrderTicketAuditEvent(
                    "REFUND_COMPLETED",
                    refundRequest.OrderId,
                    SystemActor,
                    ticketCount,
                    completedAt,
                    refundRequest.RefundId,
                    refundRequest.ActualRefund,
                    new Dictionary<string, string>
                    {
                        ["ApproveStatus"] = refundRequest.ApproveStatus,
                        ["RefundStatus"] = refundRequest.RefundStatus,
                        ["EventId"] = approvedEvent.EventId,
                    }),
                cancellationToken);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await RollbackAndClearAsync(transaction, CancellationToken.None);
            throw;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            logger.LogWarning(exception, "Concurrent refund completion detected for refund {RefundId}.", approvedEvent.RefundId);
            return await RecoverConcurrentCompletionAsync(transaction, approvedEvent, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Refund completion failed for refund {RefundId}.", approvedEvent.RefundId);
            await RollbackAndClearAsync(transaction, CancellationToken.None);
            return RefundCompletionResult.Retryable(
                "REFUND_COMPLETION_FAILED",
                "The refund could not be completed and should be retried.");
        }
    }

    private async Task<RefundCompletionResult> ApplyCompletionAsync(
        RefundRequest refundRequest,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        var actualRefund = refundRequest.ActualRefund!.Value;
        var refundItems = refundRequest.Items.OrderBy(item => item.OrderItemId).ToList();
        if (refundItems.Count == 0 ||
            refundItems.Sum(item => item.RefundBaseAmount) != refundRequest.RefundAmount ||
            refundItems.Any(item => item.OrderItem is null ||
                item.OrderItem.OrderId != refundRequest.OrderId ||
                item.OrderItem.ItemStatus != "REFUNDING" ||
                item.OrderItem.ETicket is null ||
                item.OrderItem.ETicket.TicketStatus != "REFUNDING"))
        {
            await RollbackAndClearAsync(transaction, cancellationToken);
            return RefundCompletionResult.Permanent(
                "REFUND_ITEM_DATA_INCONSISTENT",
                "The frozen refund item state is inconsistent.");
        }

        var order = await dbContext.Set<Order>()
            .SingleOrDefaultAsync(item => item.OrderId == refundRequest.OrderId, cancellationToken);
        var allOrderItems = await dbContext.Set<OrderItem>()
            .Where(item => item.OrderId == refundRequest.OrderId)
            .OrderBy(item => item.OrderItemId)
            .ToListAsync(cancellationToken);
        var payments = await dbContext.Set<Payment>()
            .AsNoTracking()
            .Where(item => item.OrderId == refundRequest.OrderId && item.PayStatus == "SUCCESS")
            .Select(item => new { item.PaymentId, item.PayAmount })
            .ToListAsync(cancellationToken);
        if (order is null || allOrderItems.Count == 0 || payments.Count != 1 ||
            payments[0].PayAmount <= 0m ||
            payments[0].PayAmount != order.TotalAmount - order.DiscountAmount ||
            allOrderItems.Sum(item => item.UnitPrice) != order.TotalAmount)
        {
            await RollbackAndClearAsync(transaction, cancellationToken);
            return RefundCompletionResult.Permanent(
                "REFUND_PAYMENT_DATA_INCONSISTENT",
                "The payment and order amounts are inconsistent.");
        }

        var orderItemIds = refundItems.Select(item => item.OrderItemId).ToArray();
        var reservationCount = await dbContext.Set<SeatReservation>()
            .AsNoTracking()
            .CountAsync(item => item.OrderItemId.HasValue &&
                orderItemIds.Contains(item.OrderItemId.Value) &&
                item.ReservationType == "ORDER" &&
                item.ReservationStatus == "ACTIVE", cancellationToken);
        if (reservationCount != orderItemIds.Length)
        {
            await RollbackAndClearAsync(transaction, cancellationToken);
            return RefundCompletionResult.Permanent(
                "REFUND_RESERVATION_DATA_INCONSISTENT",
                "Seat reservation data is inconsistent.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var paymentRows = await dbContext.Set<Payment>()
            .Where(item => item.PaymentId == payments[0].PaymentId &&
                item.OrderId == order.OrderId &&
                item.PayStatus == "SUCCESS" &&
                item.RefundAmount + actualRefund <= item.PayAmount)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.RefundAmount, item => item.RefundAmount + actualRefund)
                .SetProperty(item => item.UpdateBy, SystemActor), cancellationToken);
        if (paymentRows != 1)
        {
            await RollbackAndClearAsync(transaction, cancellationToken);
            return RefundCompletionResult.Retryable(
                "REFUND_PAYMENT_AMOUNT_CONFLICT",
                "Payment refund amount changed.");
        }

        var releasedRows = await dbContext.Set<SeatReservation>()
            .Where(item => item.OrderItemId.HasValue &&
                orderItemIds.Contains(item.OrderItemId.Value) &&
                item.ReservationType == "ORDER" &&
                item.ReservationStatus == "ACTIVE")
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.ReservationStatus, "RELEASED")
                .SetProperty(item => item.CancelTime, now)
                .SetProperty(item => item.UpdateBy, SystemActor), cancellationToken);
        if (releasedRows != orderItemIds.Length)
        {
            await RollbackAndClearAsync(transaction, cancellationToken);
            return RefundCompletionResult.Retryable(
                "REFUND_RESERVATION_CONFLICT",
                "Seat reservations changed during completion.");
        }

        foreach (var refundItem in refundItems)
        {
            refundItem.OrderItem!.ItemStatus = "REFUNDED";
            refundItem.OrderItem.UpdateBy = SystemActor;
            refundItem.OrderItem.ETicket!.TicketStatus = "REFUNDED";
            refundItem.OrderItem.ETicket.UpdateBy = SystemActor;
        }
        order.OrderStatus = allOrderItems.All(item => item.ItemStatus == "REFUNDED")
            ? "REFUNDED"
            : "PART_REFUND";
        order.UpdateBy = SystemActor;
        refundRequest.RefundStatus = "COMPLETED";
        refundRequest.CompleteTime = now;
        refundRequest.UpdateBy = SystemActor;

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return RefundCompletionResult.Completed();
    }

    private async Task<RefundCompletionResult> RecoverConcurrentCompletionAsync(
        IDbContextTransaction transaction,
        RefundApprovedEvent approvedEvent,
        CancellationToken cancellationToken)
    {
        await RollbackAndClearAsync(transaction, CancellationToken.None);
        try
        {
            var latest = await dbContext.Set<RefundRequest>()
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.RefundId == approvedEvent.RefundId, cancellationToken);
            if (latest is not null &&
                ValidatePersistentIdentity(
                    approvedEvent,
                    latest.OrderId,
                    latest.UserId,
                    latest.RefundNo,
                    latest.ActualRefund) is null &&
                latest.ApproveStatus == "APPROVED" &&
                latest.RefundStatus == "COMPLETED")
            {
                return RefundCompletionResult.AlreadyCompleted();
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Refund completion recovery read failed for refund {RefundId}.", approvedEvent.RefundId);
        }

        return RefundCompletionResult.Retryable(
            "REFUND_COMPLETION_CONFLICT",
            "The refund completion conflicted with another operation.");
    }

    private async Task<RefundCompletionResult> RollbackRetryableAsync(
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await RollbackAndClearAsync(transaction, cancellationToken);
        return RefundCompletionResult.Retryable(
            "REFUND_COMPLETION_CONFLICT",
            "The refund request changed during completion.");
    }

    private async Task RollbackAndClearAsync(
        IDbContextTransaction transaction,
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
            logger.LogWarning(exception, "Refund completion audit failed for refund {RefundId}.", auditEvent.RefundId);
        }
    }

    private static bool IsStructurallyValid(RefundApprovedEvent approvedEvent) =>
        approvedEvent.EventType == RefundApprovedEvent.TypeName &&
        Guid.TryParseExact(approvedEvent.EventId, "D", out _) &&
        approvedEvent.RefundId > 0 &&
        approvedEvent.OrderId > 0 &&
        approvedEvent.UserId > 0 &&
        !string.IsNullOrWhiteSpace(approvedEvent.RefundNo) &&
        approvedEvent.ActualRefund > 0m;

    private static RefundCompletionResult? ValidatePersistentIdentity(
        RefundApprovedEvent approvedEvent,
        long orderId,
        long userId,
        string refundNo,
        decimal? actualRefund)
    {
        if (approvedEvent.OrderId != orderId ||
            approvedEvent.UserId != userId ||
            !string.Equals(approvedEvent.RefundNo, refundNo, StringComparison.Ordinal))
        {
            return RefundCompletionResult.Permanent(
                "REFUND_EVENT_IDENTITY_MISMATCH",
                "The refund event identifiers do not match the persisted aggregate.");
        }

        if (!actualRefund.HasValue || approvedEvent.ActualRefund != actualRefund.Value)
        {
            return RefundCompletionResult.Permanent(
                "REFUND_EVENT_AMOUNT_MISMATCH",
                "The refund event amount does not match the frozen database amount.");
        }

        return null;
    }
}
