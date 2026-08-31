using ShowtimeBackend.Common;

namespace ShowtimeBackend.DTOs.OrderTicket;

public sealed record TicketResponse(
    long ETicketId,
    string ETicketNo,
    long OrderItemId,
    ETicketStatus TicketStatus,
    string QrCode);
