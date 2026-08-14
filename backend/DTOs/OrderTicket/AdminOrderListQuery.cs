using ShowtimeBackend.Common;

namespace ShowtimeBackend.DTOs.OrderTicket;

public sealed record AdminOrderListQuery(
    OrderStatus? Status,
    string? Keyword,
    int Page = 1,
    int PageSize = 20);

public sealed record AdminOrderSummaryResponse(
    long OrderId,
    string OrderNo,
    long UserId,
    string UserName,
    string? Nickname,
    string Phone,
    long SessionId,
    decimal TotalAmount,
    decimal DiscountAmount,
    int TicketCount,
    OrderStatus OrderStatus,
    DateTime ExpireTime,
    DateTime CreateTime);

public sealed record PagedAdminOrderResponse(
    IReadOnlyList<AdminOrderSummaryResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);
