namespace ShowtimeBackend.Services.OrderTicket;

public interface IExchangeLockCoordinator
{
    Task<bool> LockExchangeRequestAsync(long exchangeId, CancellationToken cancellationToken);

    Task<bool> LockOrderAsync(long orderId, CancellationToken cancellationToken);

    Task<bool> LockOrderItemAsync(long orderItemId, CancellationToken cancellationToken);

    Task<bool> LockETicketAsync(long eTicketId, CancellationToken cancellationToken);

    Task<bool> LockSeatReservationAsync(
        long seatReservationId,
        CancellationToken cancellationToken);

    Task<bool> LockSeatLockAsync(long seatLockId, CancellationToken cancellationToken);
}
