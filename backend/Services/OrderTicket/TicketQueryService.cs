using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Common;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.OrderTicket;

namespace ShowtimeBackend.Services.OrderTicket;

public sealed class TicketQueryService(AppDbContext dbContext) : ITicketQueryService
{
    public async Task<OrderTicketResult<IReadOnlyList<TicketResponse>>> ListForOwnerAsync(
        long userId,
        long orderId,
        CancellationToken cancellationToken)
    {
        var orderExists = await dbContext.Set<Order>()
            .AsNoTracking()
            .AnyAsync(
                order => order.OrderId == orderId && order.UserId == userId,
                cancellationToken);
        if (!orderExists)
        {
            return OrderTicketResult<IReadOnlyList<TicketResponse>>.Fail(
                OrderTicketFailure.NotFound,
                "TICKET_ORDER_NOT_FOUND",
                "The order does not exist.");
        }

        var entities = await dbContext.Set<ETicket>()
            .AsNoTracking()
            .Where(ticket =>
                ticket.UserId == userId &&
                ticket.OrderItem!.OrderId == orderId)
            .OrderBy(ticket => ticket.OrderItemId)
            .ToListAsync(cancellationToken);

        var tickets = entities
            .Select(ticket => new TicketResponse(
                ticket.ETicketId,
                ticket.ETicketNo,
                ticket.OrderItemId,
                ticket.TicketStatus.ToEnum<ETicketStatus>(),
                ticket.QrCode))
            .ToList();
        return OrderTicketResult<IReadOnlyList<TicketResponse>>.Success(tickets);
    }
}
