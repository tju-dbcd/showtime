using ShowtimeBackend.Common;

namespace ShowtimeBackend.DTOs.OrderTicket;

public sealed record TicketIssuanceResponse(
    long OrderId,
    OrderStatus OrderStatus,
    int CreatedTicketCount,
    int ExistingTicketCount,
    int TotalTicketCount,
    DateTime IssueTime);
