using ShowtimeBackend.DTOs.OrderTicket;

namespace ShowtimeBackend.Services.OrderTicket;

public interface IExchangeReviewService
{
    Task<OrderTicketResult<PagedExchangeResponse>> ListAsync(
        AdminExchangeListQuery query, CancellationToken cancellationToken = default);

    Task<OrderTicketResult<ExchangeResponse>> GetAsync(
        long exchangeId, CancellationToken cancellationToken = default);

    Task<OrderTicketResult<ExchangeResponse>> RejectAsync(
        string actor, long exchangeId, RejectExchangeRequest request,
        CancellationToken cancellationToken = default);

    Task<OrderTicketResult<ExchangeResponse>> ApproveAsync(
        string actor, long exchangeId, ApproveExchangeRequest request,
        CancellationToken cancellationToken = default);

    Task<OrderTicketResult<ExchangeResponse>> ExpireAsync(
        long exchangeId, string actor, CancellationToken cancellationToken = default);
}
