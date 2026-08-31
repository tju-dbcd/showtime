using ShowtimeBackend.Common;

namespace ShowtimeBackend.DTOs.OrderTicket;

public sealed record OrderListQuery(
    OrderStatus? Status,
    int Page = 1,
    int PageSize = 20);

public sealed record OrderSummaryResponse(
    long OrderId,
    string OrderNo,
    long SessionId,
    OrderType OrderType,
    long? ParentOrderId,
    decimal TotalAmount,
    decimal DiscountAmount,
    int TicketCount,
    OrderStatus OrderStatus,
    bool CanPay,
    bool CanCancel,
    DateTime ExpireTime,
    DateTime CreateTime);

public sealed record PagedOrderResponse(
    IReadOnlyList<OrderSummaryResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);
