using ShowtimeBackend.DTOs.OrderTicket;

namespace ShowtimeBackend.Services.OrderTicket;

public interface IRefundReviewService
{
    Task<OrderTicketResult<PagedRefundResponse>> ListAsync(
        AdminRefundListQuery query,
        CancellationToken cancellationToken);

    Task<OrderTicketResult<RefundResponse>> GetAsync(
        long refundId,
        CancellationToken cancellationToken);

    Task<OrderTicketResult<RefundResponse>> ApproveAsync(
        string actor,
        long refundId,
        ApproveRefundRequest request,
        CancellationToken cancellationToken);

    Task<OrderTicketResult<RefundResponse>> RejectAsync(
        string actor,
        long refundId,
        RejectRefundRequest request,
        CancellationToken cancellationToken);
}
