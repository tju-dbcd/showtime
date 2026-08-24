using ShowtimeBackend.DTOs.OrderTicket;

namespace ShowtimeBackend.Services.OrderTicket;

public interface IRefundApplicationService
{
    Task<OrderTicketResult<RefundQuoteResponse>> QuoteAsync(
        long userId,
        long orderId,
        RefundQuoteRequest request,
        CancellationToken cancellationToken);
}
