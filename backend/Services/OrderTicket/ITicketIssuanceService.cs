using ShowtimeBackend.Entities.OrderTicket;

namespace ShowtimeBackend.Services.OrderTicket;

public interface ITicketIssuanceService
{
    OrderTicketResult<TicketIssuanceOutcome> Issue(
        Order order,
        TicketIssuanceContext context,
        string actor,
        DateTimeOffset operationTime);
}
