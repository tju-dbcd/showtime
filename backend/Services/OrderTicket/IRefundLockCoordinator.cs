namespace ShowtimeBackend.Services.OrderTicket;

public interface IRefundLockCoordinator
{
    Task<bool> LockRefundRequestAsync(
        long refundId,
        CancellationToken cancellationToken);

    Task<bool> LockOrderAsync(
        long orderId,
        CancellationToken cancellationToken);
}
