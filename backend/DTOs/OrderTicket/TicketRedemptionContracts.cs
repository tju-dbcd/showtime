using ShowtimeBackend.Common;

namespace ShowtimeBackend.DTOs.OrderTicket;

public sealed record RedeemTicketRequest(
    string? QrCode,
    string? CheckDevice);

public sealed record TicketRedemptionResponse(
    long ETicketId,
    string ETicketNo,
    long OrderId,
    long OrderItemId,
    long SessionId,
    ETicketStatus TicketStatus,
    DateTime CheckTime,
    string CheckDevice,
    string CheckBy);
