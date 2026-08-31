using ShowtimeBackend.DTOs.OrderTicket;

namespace ShowtimeBackend.Services.OrderTicket;

public interface IExchangeApplicationService
{
    Task<OrderTicketResult<ExchangeQuoteResponse>> QuoteAsync(
        long userId,
        long orderId,
        ExchangeQuoteRequest request,
        CancellationToken cancellationToken = default);

    Task<OrderTicketResult<ExchangeResponse>> CreateAsync(
        long userId,
        string actor,
        long orderId,
        CreateExchangeRequest request,
        CancellationToken cancellationToken = default);

    Task<OrderTicketResult<PagedExchangeResponse>> ListAsync(
        long userId,
        long orderId,
        ExchangeListQuery query,
        CancellationToken cancellationToken = default);

    Task<OrderTicketResult<ExchangeResponse>> GetAsync(
        long userId,
        long exchangeId,
        CancellationToken cancellationToken = default);
}
