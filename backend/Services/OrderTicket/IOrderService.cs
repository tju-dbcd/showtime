using ShowtimeBackend.DTOs.OrderTicket;

namespace ShowtimeBackend.Services.OrderTicket;

public interface IOrderService
{
    Task<OrderTicketResult<PagedOrderResponse>> ListAsync(
        long userId,
        OrderListQuery query,
        CancellationToken cancellationToken);

    Task<OrderTicketResult<OrderResponse>> GetAsync(
        long userId,
        long orderId,
        CancellationToken cancellationToken);

    Task<OrderTicketResult<OrderResponse>> CreateAsync(
        long userId,
        string actor,
        CreateOrderRequest request,
        CancellationToken cancellationToken);

    Task<OrderTicketResult<OrderResponse>> CancelAsync(
        long userId,
        string actor,
        long orderId,
        CancellationToken cancellationToken);
}
