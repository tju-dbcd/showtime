using ShowtimeBackend.Common;

namespace ShowtimeBackend.DTOs.OrderTicket;

public sealed record OrderItemResponse(
    long OrderItemId,
    long SeatId,
    long PriceStrategyId,
    long? RealNameId,
    decimal UnitPrice,
    OrderItemStatus ItemStatus);

public sealed record ETicketSummaryResponse(
    long ETicketId,
    string ETicketNo,
    long OrderItemId,
    ETicketStatus TicketStatus);

public sealed record OrderResponse(
    long OrderId,
    string OrderNo,
    long SessionId,
    decimal TotalAmount,
    decimal DiscountAmount,
    int TicketCount,
    OrderStatus OrderStatus,
    DateTime ExpireTime,
    DateTime? PayTime,
    DateTime? IssueTime,
    DateTime? CancelTime,
    string Source,
    string? Remark,
    IReadOnlyList<OrderItemResponse> Items,
    IReadOnlyList<PaymentResponse> Payments,
    IReadOnlyList<ETicketSummaryResponse> Tickets,
    DateTime CreateTime);
