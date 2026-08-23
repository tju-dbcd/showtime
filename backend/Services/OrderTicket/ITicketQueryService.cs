using ShowtimeBackend.DTOs.OrderTicket;

namespace ShowtimeBackend.Services.OrderTicket;

public interface ITicketQueryService
{
    Task<OrderTicketResult<IReadOnlyList<TicketResponse>>> ListForOwnerAsync(
        long userId,
        long orderId,
        CancellationToken cancellationToken);
}
