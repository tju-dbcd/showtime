using ShowtimeBackend.DTOs.OrderTicket;

namespace ShowtimeBackend.Services.OrderTicket;

public interface ITicketRedemptionService
{
    Task<OrderTicketResult<TicketRedemptionResponse>> RedeemAsync(
        string actor,
        RedeemTicketRequest request,
        CancellationToken cancellationToken);
}
