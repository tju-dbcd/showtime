using ShowtimeBackend.DTOs.OrderTicket;

namespace ShowtimeBackend.Services.OrderTicket;

public interface IRefundApplicationService
{
    Task<OrderTicketResult<PagedRefundResponse>> ListAsync(
        long userId,
        long orderId,
        RefundListQuery query,
        CancellationToken cancellationToken);

    Task<OrderTicketResult<RefundResponse>> GetAsync(
        long userId,
        long refundId,
        CancellationToken cancellationToken);

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
