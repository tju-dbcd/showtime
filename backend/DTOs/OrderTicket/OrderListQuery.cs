namespace ShowtimeBackend.DTOs.OrderTicket;

public sealed record OrderListQuery(
    string? Status,
    int Page = 1,
    int PageSize = 20);

public sealed record OrderSummaryResponse(
    long OrderId,
    string OrderNo,
    long SessionId,
    decimal TotalAmount,
    decimal DiscountAmount,
    int TicketCount,
    string OrderStatus,
    DateTime ExpireTime,
    DateTime CreateTime);

public sealed record PagedOrderResponse(
    IReadOnlyList<OrderSummaryResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);
