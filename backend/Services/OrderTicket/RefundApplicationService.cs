using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Entities.SeatZone;

namespace ShowtimeBackend.Services.OrderTicket;

public sealed class RefundApplicationService(
    AppDbContext dbContext,
    RefundPolicyEngine policyEngine,
    TimeProvider timeProvider) : IRefundApplicationService
{
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
                "REFUND_ITEMS_REQUIRED",
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
                "REFUND_ORDER_STATUS_INVALID",
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
            return NotFound<RefundQuoteResponse>(
                "REFUND_ORDER_ITEM_NOT_FOUND",
                "An order item does not belong to the order.");
        }

        if (selectedItems.Any(item => item.ItemStatus != "NORMAL"))
        {
            return Conflict<RefundQuoteResponse>(
                "REFUND_ORDER_ITEM_STATUS_INVALID",
                "An order item status does not allow a refund.");
        }

        var tickets = await dbContext.Set<ETicket>()
            .AsNoTracking()
            .Where(item => selectedItemIds.Contains(item.OrderItemId))
            .ToListAsync(cancellationToken);
        if (tickets.Count != selectedItemIds.Count ||
            tickets.Any(item => item.TicketStatus != "UNUSED"))
        {
            return Conflict<RefundQuoteResponse>(
                "REFUND_TICKET_STATUS_INVALID",
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
                "REFUND_SEAT_RESERVATION_INVALID",
                "Each order item must have one active order reservation.");
        }

        var hasRefundRelation = await dbContext.Set<RefundItem>()
            .AsNoTracking()
            .AnyAsync(item => selectedItemIds.Contains(item.OrderItemId), cancellationToken);
        var hasExchangeRelation = await dbContext.Set<ExchangeItem>()
            .AsNoTracking()
            .AnyAsync(item =>
                selectedItemIds.Contains(item.OrderItemId) ||
                selectedItemIds.Contains(item.NewOrderItemId),
                cancellationToken);
        if (hasRefundRelation || hasExchangeRelation)
        {
            return Conflict<RefundQuoteResponse>(
                "REFUND_ITEM_ALREADY_RELATED",
                "An order item already belongs to a refund or exchange.");
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
}
