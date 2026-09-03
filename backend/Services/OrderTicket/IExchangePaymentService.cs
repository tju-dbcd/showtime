using ShowtimeBackend.DTOs.OrderTicket;

namespace ShowtimeBackend.Services.OrderTicket;

public interface IExchangePaymentService
{
    Task<OrderTicketResult<ExchangePaymentResponse>> PayAsync(
        long userId, string actor, long exchangeId, ExchangePaymentRequest request,
        CancellationToken cancellationToken = default);
}
