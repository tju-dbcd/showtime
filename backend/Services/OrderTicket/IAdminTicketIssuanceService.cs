using ShowtimeBackend.DTOs.OrderTicket;

namespace ShowtimeBackend.Services.OrderTicket;

public interface IAdminTicketIssuanceService
{
    Task<OrderTicketResult<TicketIssuanceResponse>> IssueAsync(
        string actor,
        long orderId,
        CancellationToken cancellationToken);
}
