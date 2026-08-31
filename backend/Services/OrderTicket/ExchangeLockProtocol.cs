using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Data;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Entities.SeatZone;

namespace ShowtimeBackend.Services.OrderTicket;

internal static class ExchangeLockProtocol
{
    public static async Task<bool> LockItemsTicketsAndReservationsAsync(
        AppDbContext dbContext,
        IExchangeLockCoordinator lockCoordinator,
        IEnumerable<long> orderItemIds,
        CancellationToken cancellationToken)
    {
        var itemIds = orderItemIds.Distinct().OrderBy(id => id).ToArray();
        foreach (var itemId in itemIds)
        {
            if (!await lockCoordinator.LockOrderItemAsync(itemId, cancellationToken))
                return false;
        }

        var ticketIds = await dbContext.Set<ETicket>()
            .AsNoTracking()
            .Where(ticket => itemIds.Contains(ticket.OrderItemId))
            .Select(ticket => ticket.ETicketId)
            .OrderBy(id => id)
            .ToListAsync(cancellationToken);
        foreach (var ticketId in ticketIds)
        {
            if (!await lockCoordinator.LockETicketAsync(ticketId, cancellationToken))
                return false;
        }

        var reservationIds = await dbContext.Set<SeatReservation>()
            .AsNoTracking()
            .Where(reservation => reservation.OrderItemId.HasValue &&
                                  itemIds.Contains(reservation.OrderItemId.Value))
            .Select(reservation => reservation.SeatReservationId)
            .OrderBy(id => id)
            .ToListAsync(cancellationToken);
        foreach (var reservationId in reservationIds)
        {
            if (!await lockCoordinator.LockSeatReservationAsync(
                    reservationId,
                    cancellationToken))
            {
                return false;
            }
        }

        return true;
    }
}
