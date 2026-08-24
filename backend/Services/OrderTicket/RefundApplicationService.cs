using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ShowtimeBackend.Common;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Entities.SeatZone;

namespace ShowtimeBackend.Services.OrderTicket;

public sealed class RefundApplicationService : IRefundApplicationService
{
    private readonly AppDbContext dbContext;
    private readonly RefundPolicyEngine policyEngine;
    private readonly TimeProvider timeProvider;
    private readonly IRefundLockCoordinator? lockCoordinator;
    private readonly ILogger<RefundApplicationService> logger;
    private readonly IOrderTicketAuditSink auditSink;

    public RefundApplicationService(
        AppDbContext dbContext,
        RefundPolicyEngine policyEngine,
        TimeProvider timeProvider)
        : this(
            dbContext,
            policyEngine,
            timeProvider,
            null,
            NullLogger<RefundApplicationService>.Instance,
            new NullOrderTicketAuditSink())
    {
    }

    public RefundApplicationService(
        AppDbContext dbContext,
        RefundPolicyEngine policyEngine,
        TimeProvider timeProvider,
        IRefundLockCoordinator? lockCoordinator,
        ILogger<RefundApplicationService> logger,
        IOrderTicketAuditSink auditSink)
    {
        this.dbContext = dbContext;
        this.policyEngine = policyEngine;
        this.timeProvider = timeProvider;
        this.lockCoordinator = lockCoordinator;
        this.logger = logger;
        this.auditSink = auditSink;
    }

    public async Task<OrderTicketResult<RefundResponse>> CreateAsync(
        long userId,
        string actor,
        long orderId,
        CreateRefundRequest request,
        CancellationToken cancellationToken)
    {
        var coordinator = lockCoordinator ?? throw new InvalidOperationException(
            "A refund lock coordinator is required to create a refund request.");
        if (request is null)
        {
            return Invalid<RefundResponse>(
                "REFUND_REASON_INVALID",
                "Refund reason must contain between 1 and 500 characters.");
        }

        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > 500)
        {
            return Invalid<RefundResponse>(
                "REFUND_REASON_INVALID",
                "Refund reason must contain between 1 and 500 characters.");
        }

        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(cancellationToken);
        if (!await coordinator.LockOrderAsync(orderId, cancellationToken))
        {
            return NotFound<RefundResponse>(
                "REFUND_ORDER_NOT_FOUND",
                "The order does not exist.");
        }

        dbContext.ChangeTracker.Clear();
        var quoteResult = await QuoteAsync(
            userId,
            orderId,
            new RefundQuoteRequest(request.OrderItemIds),
            cancellationToken);
        if (!quoteResult.IsSuccess)
        {
            if (quoteResult.ErrorCode == "REFUND_ITEM_NOT_ELIGIBLE" &&
                await HasExistingRefundItemAsync(
                    request.OrderItemIds,
                    cancellationToken))
            {
                return Conflict<RefundResponse>(
                    "REFUND_ITEM_ALREADY_REQUESTED",
                    "An order item already belongs to a refund request.");
            }

            return OrderTicketResult<RefundResponse>.Fail(
                quoteResult.Failure,
                quoteResult.ErrorCode!,
                quoteResult.Message!);
        }

        var quote = quoteResult.Value!;
        var selectedItemIds = quote.Items
            .Select(item => item.OrderItemId)
            .ToHashSet();
        var selectedItems = await dbContext.Set<OrderItem>()
            .Include(item => item.ETicket)
            .Where(item => selectedItemIds.Contains(item.OrderItemId))
            .OrderBy(item => item.OrderItemId)
            .ToListAsync(cancellationToken);

        var refundRequest = new RefundRequest
        {
            RefundNo = CreateRefundNumber(quote.QuotedAt),
            OrderId = orderId,
            UserId = userId,
            RefundType = quote.RefundType.ToString(),
            RefundReason = reason,
            RefundAmount = quote.RefundAmount,
            ActualRefund = quote.ActualRefund,
            FeeRate = quote.FeeRate,
            AppliedPolicyId = quote.AppliedPolicyId,
            AppliedServiceFee = quote.AppliedServiceFee,
            ApproveStatus = "PENDING",
            RefundStatus = "PENDING",
            CreateBy = actor,
            UpdateBy = actor,
        };

        foreach (var quoteItem in quote.Items)
        {
            refundRequest.Items.Add(new RefundItem
            {
                OrderItemId = quoteItem.OrderItemId,
                RefundBaseAmount = quoteItem.RefundBaseAmount,
                CreateBy = actor,
                UpdateBy = actor,
            });
        }

        foreach (var item in selectedItems)
        {
            item.ItemStatus = "REFUNDING";
            item.UpdateBy = actor;
            item.ETicket!.TicketStatus = "REFUNDING";
            item.ETicket.UpdateBy = actor;
        }

        dbContext.Add(refundRequest);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            logger.LogWarning(
                exception,
                "Concurrent refund application detected for order {OrderId}.",
                orderId);
            await RollbackAndClearAsync(transaction, cancellationToken);
            return Conflict<RefundResponse>(
                "REFUND_CREATE_CONFLICT",
                "The refund request conflicted with another operation.");
        }
        catch (DbUpdateException exception)
        {
            var constraint = RefundConstraintClassifier.Classify(exception);
            await RollbackAndClearAsync(transaction, cancellationToken);
            if (constraint == RefundUniqueConstraint.OrderItem)
            {
                logger.LogWarning(
                    exception,
                    "Duplicate refund item detected for order {OrderId}.",
                    orderId);
                return Conflict<RefundResponse>(
                    "REFUND_ITEM_ALREADY_REQUESTED",
                    "An order item already belongs to a refund request.");
            }

            logger.LogError(
                exception,
                "Refund application persistence failed for order {OrderId}.",
                orderId);
            return Internal<RefundResponse>(
                "REFUND_CREATE_FAILED",
                "The refund request could not be created.");
        }

        var response = new RefundResponse(
            refundRequest.RefundId,
            refundRequest.RefundNo,
            refundRequest.OrderId,
            refundRequest.UserId,
            refundRequest.RefundType.ToEnum<RefundType>(),
            refundRequest.RefundReason,
            refundRequest.AppliedPolicyId,
            quote.PolicyName,
            refundRequest.RefundAmount,
            refundRequest.FeeRate,
            refundRequest.AppliedServiceFee,
            refundRequest.ActualRefund,
            refundRequest.ApproveStatus.ToEnum<RefundApproveStatus>(),
            refundRequest.RefundStatus.ToEnum<RefundStatus>(),
            refundRequest.ReviewBy,
            refundRequest.ReviewTime,
            refundRequest.ReviewRemark,
            refundRequest.CompleteTime,
            refundRequest.CreateTime,
            refundRequest.Items
                .OrderBy(item => item.OrderItemId)
                .Select(item =>
                {
                    var orderItem = selectedItems.Single(
                        selected => selected.OrderItemId == item.OrderItemId);
                    return new RefundItemResponse(
                        item.RefundItemId,
                        item.OrderItemId,
                        item.RefundBaseAmount,
                        orderItem.ItemStatus.ToEnum<OrderItemStatus>(),
                        orderItem.ETicket!.TicketStatus.ToEnum<ETicketStatus>());
                })
                .ToList());

        await WriteAuditSafelyAsync(
            new OrderTicketAuditEvent(
                "REFUND_REQUESTED",
                orderId,
                actor,
                response.Items.Count,
                quote.QuotedAt,
                refundRequest.RefundId,
                refundRequest.ActualRefund,
                new Dictionary<string, string>
                {
                    ["ApproveStatus"] = refundRequest.ApproveStatus,
                    ["RefundStatus"] = refundRequest.RefundStatus,
                    ["AppliedPolicyId"] = refundRequest.AppliedPolicyId!.Value.ToString(),
                }),
            cancellationToken);

        return OrderTicketResult<RefundResponse>.Success(response);
    }

    public async Task<OrderTicketResult<RefundQuoteResponse>> QuoteAsync(
        long userId,
        long orderId,
        RefundQuoteRequest request,
        CancellationToken cancellationToken)
    {
        var quotedAt = timeProvider.GetUtcNow().UtcDateTime;
        var order = await dbContext.Set<Order>()
            .AsNoTracking()
            .Include(item => item.Items)
            .SingleOrDefaultAsync(
                item => item.OrderId == orderId && item.UserId == userId,
                cancellationToken);
        if (order is null)
        {
            return NotFound<RefundQuoteResponse>(
                "REFUND_ORDER_NOT_FOUND",
                "The order does not exist.");
        }

        if (request?.OrderItemIds is null || request.OrderItemIds.Count == 0)
        {
            return Invalid<RefundQuoteResponse>(
                "REFUND_ITEM_IDS_REQUIRED",
                "At least one order item is required.");
        }

        var selectedItemIds = request.OrderItemIds.ToHashSet();
        if (selectedItemIds.Count != request.OrderItemIds.Count)
        {
            return Invalid<RefundQuoteResponse>(
                "REFUND_ITEM_IDS_DUPLICATED",
                "Order item IDs must be unique.");
        }

        if (order.OrderStatus is not ("ISSUED" or "PART_REFUND"))
        {
            return Conflict<RefundQuoteResponse>(
                "REFUND_ORDER_NOT_ELIGIBLE",
                "The order status does not allow a refund.");
        }

        var session = await dbContext.Set<Entities.ShowSession.ShowSession>()
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.SessionId == order.SessionId, cancellationToken);
        if (session is null)
        {
            return Conflict<RefundQuoteResponse>(
                "REFUND_SESSION_INVALID",
                "The order session does not exist.");
        }

        if (session.StartTime <= quotedAt)
        {
            return Conflict<RefundQuoteResponse>(
                "REFUND_SESSION_STARTED",
                "The session has already started.");
        }

        var allItems = order.Items.ToList();
        var selectedItems = allItems
            .Where(item => selectedItemIds.Contains(item.OrderItemId))
            .ToList();
        if (selectedItems.Count != selectedItemIds.Count)
        {
            return Conflict<RefundQuoteResponse>(
                "REFUND_ITEM_NOT_ELIGIBLE",
                "An order item is not eligible for a refund.");
        }

        if (selectedItems.Any(item => item.ItemStatus != "NORMAL"))
        {
            return Conflict<RefundQuoteResponse>(
                "REFUND_ITEM_NOT_ELIGIBLE",
                "An order item is not eligible for a refund.");
        }

        var tickets = await dbContext.Set<ETicket>()
            .AsNoTracking()
            .Where(item => selectedItemIds.Contains(item.OrderItemId))
            .ToListAsync(cancellationToken);
        if (tickets.Count != selectedItemIds.Count ||
            tickets.Any(item => item.TicketStatus != "UNUSED"))
        {
            return Conflict<RefundQuoteResponse>(
                "REFUND_TICKET_NOT_UNUSED",
                "Each order item must have one unused ticket.");
        }

        var reservations = await dbContext.Set<SeatReservation>()
            .AsNoTracking()
            .Where(item => item.OrderItemId.HasValue &&
                selectedItemIds.Contains(item.OrderItemId.Value))
            .ToListAsync(cancellationToken);
        var activeOrderReservationCounts = selectedItemIds.ToDictionary(
            itemId => itemId,
            itemId => reservations.Count(item =>
                item.OrderItemId == itemId &&
                item.ReservationType == "ORDER" &&
                item.ReservationStatus == "ACTIVE"));
        if (activeOrderReservationCounts.Values.Any(count => count > 1))
        {
            return Conflict<RefundQuoteResponse>(
                "REFUND_RESERVATION_DATA_INCONSISTENT",
                "An order item has duplicate active order reservations.");
        }

        if (activeOrderReservationCounts.Values.Any(count => count != 1))
        {
            return Conflict<RefundQuoteResponse>(
                "REFUND_RESERVATION_DATA_INCONSISTENT",
                "Each order item must have one active order reservation.");
        }

        var hasRefundRelation = await dbContext.Set<RefundItem>()
            .AsNoTracking()
            .AnyAsync(item => selectedItemIds.Contains(item.OrderItemId), cancellationToken);
        if (hasRefundRelation)
        {
            return Conflict<RefundQuoteResponse>(
                "REFUND_ITEM_ALREADY_REQUESTED",
                "An order item already belongs to a refund request.");
        }

        var hasExchangeRelation = await dbContext.Set<ExchangeItem>()
            .AsNoTracking()
            .AnyAsync(item =>
                selectedItemIds.Contains(item.OrderItemId) ||
                selectedItemIds.Contains(item.NewOrderItemId),
                cancellationToken);
        if (hasExchangeRelation)
        {
            return Conflict<RefundQuoteResponse>(
                "REFUND_ITEM_EXCHANGE_CONFLICT",
                "An order item already belongs to an exchange.");
        }

        var successfulPayments = await dbContext.Set<Payment>()
            .AsNoTracking()
            .Where(item => item.OrderId == orderId && item.PayStatus == "SUCCESS")
            .Select(item => new { item.PaymentId, item.PayAmount })
            .ToListAsync(cancellationToken);
        if (successfulPayments.Count != 1 || successfulPayments[0].PayAmount <= 0m ||
            successfulPayments[0].PayAmount != order.TotalAmount - order.DiscountAmount ||
            allItems.Sum(item => item.UnitPrice) != order.TotalAmount ||
            order.TotalAmount <= 0m)
        {
            return Conflict<RefundQuoteResponse>(
                "REFUND_PAYMENT_DATA_INCONSISTENT",
                "Payment data is inconsistent.");
        }

        var policies = await dbContext.Set<RefundPolicy>()
            .AsNoTracking()
            .Where(item => item.Status == 1 &&
                (item.ShowId == null || item.ShowId == session.ShowId))
            .Select(item => new RefundPolicyRule(
                item.PolicyId,
                item.ShowId,
                item.PolicyName,
                item.RefundDeadlineHour,
                item.RefundRate,
                item.ServiceFee,
                item.Priority,
                item.Status))
            .ToListAsync(cancellationToken);
        var quote = policyEngine.Quote(new RefundQuoteInput(
            quotedAt,
            session.StartTime,
            session.ShowId,
            successfulPayments[0].PayAmount,
            allItems
                .Select(item => new RefundAllocationItem(item.OrderItemId, item.UnitPrice))
                .ToList(),
            selectedItemIds,
            policies));
        if (quote is null)
        {
            return Conflict<RefundQuoteResponse>(
                "REFUND_POLICY_NOT_FOUND",
                "No refund policy applies to this order.");
        }

        if (quote.ActualRefund <= 0m)
        {
            return Conflict<RefundQuoteResponse>(
                "REFUND_AMOUNT_NOT_POSITIVE",
                "The actual refund amount must be positive.");
        }

        return OrderTicketResult<RefundQuoteResponse>.Success(new RefundQuoteResponse(
            quote.QuotedAt,
            order.OrderId,
            quote.RefundType,
            quote.AppliedPolicyId,
            quote.PolicyName,
            quote.RefundAmount,
            quote.FeeRate,
            quote.AppliedServiceFee,
            quote.ActualRefund,
            quote.Items
                .OrderBy(item => item.OrderItemId)
                .Select(item => new RefundQuoteItemResponse(
                    item.OrderItemId,
                    item.RefundBaseAmount))
                .ToList()));
    }

    private static OrderTicketResult<T> Invalid<T>(string code, string message) =>
        OrderTicketResult<T>.Fail(OrderTicketFailure.InvalidRequest, code, message);

    private static OrderTicketResult<T> NotFound<T>(string code, string message) =>
        OrderTicketResult<T>.Fail(OrderTicketFailure.NotFound, code, message);

    private static OrderTicketResult<T> Conflict<T>(string code, string message) =>
        OrderTicketResult<T>.Fail(OrderTicketFailure.Conflict, code, message);

    private static OrderTicketResult<T> Internal<T>(string code, string message) =>
        OrderTicketResult<T>.Fail(OrderTicketFailure.Internal, code, message);

    private Task<bool> HasExistingRefundItemAsync(
        IReadOnlyList<long> orderItemIds,
        CancellationToken cancellationToken)
    {
        var selectedItemIds = orderItemIds.ToHashSet();
        return dbContext.Set<RefundItem>()
            .AsNoTracking()
            .AnyAsync(
                item => selectedItemIds.Contains(item.OrderItemId),
                cancellationToken);
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
                "Order-ticket audit sink failed for refund {RefundId} on order {OrderId}.",
                auditEvent.RefundId,
                auditEvent.OrderId);
        }
    }

    private static string CreateRefundNumber(DateTime utcNow) =>
        $"REF{utcNow:yyyyMMddHHmmssfff}{Guid.NewGuid():N}"[..30]
            .ToUpperInvariant();
}
