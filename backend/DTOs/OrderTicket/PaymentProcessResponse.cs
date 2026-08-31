using ShowtimeBackend.Common;

namespace ShowtimeBackend.DTOs.OrderTicket;

public sealed record PaymentProcessResponse(
    PaymentResponse Payment,
    OrderStatus OrderStatus,
    int IssuedTicketCount);
