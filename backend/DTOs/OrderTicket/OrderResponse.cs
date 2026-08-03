namespace ShowtimeBackend.DTOs.OrderTicket;

public sealed record OrderItemResponse(
    long OrderItemId,
    long SeatId,
    long PriceStrategyId,
    long? RealNameId,
    decimal UnitPrice,
    string ItemStatus);

public sealed record ETicketSummaryResponse(
    long ETicketId,
    string ETicketNo,
    long OrderItemId,
    string TicketStatus);

public sealed record OrderResponse(
    long OrderId,
    string OrderNo,
    long SessionId,
    decimal TotalAmount,
    decimal DiscountAmount,
    int TicketCount,
    string OrderStatus,
    DateTime ExpireTime,
    DateTime? PayTime,
    DateTime? CancelTime,
    string Source,
    string? Remark,
    IReadOnlyList<OrderItemResponse> Items,
    IReadOnlyList<PaymentResponse> Payments,
    IReadOnlyList<ETicketSummaryResponse> Tickets);
