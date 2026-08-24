using ShowtimeBackend.DTOs.OrderTicket;

namespace ShowtimeBackend.Services.OrderTicket;

public interface IRefundApplicationService
{
    Task<OrderTicketResult<RefundResponse>> CreateAsync(
        long userId,
        string actor,
        long orderId,
        CreateRefundRequest request,
        CancellationToken cancellationToken);

    Task<OrderTicketResult<RefundQuoteResponse>> QuoteAsync(
        long userId,
        long orderId,
        RefundQuoteRequest request,
        CancellationToken cancellationToken);
}
