using Microsoft.AspNetCore.SignalR;

namespace ShowtimeBackend.Services.OrderTicket.Messaging;

public interface IOrderNotificationDispatcher
{
    Task DispatchOrderCreatedAsync(
        OrderCreatedEvent notification,
        CancellationToken cancellationToken);

    Task DispatchRefundStatusChangedAsync(
        RefundStatusChangedEvent statusEvent,
        CancellationToken cancellationToken);
}

public sealed class SignalROrderNotificationDispatcher(
    IHubContext<OrderNotificationsHub> hubContext) : IOrderNotificationDispatcher
{
    public Task DispatchOrderCreatedAsync(
        OrderCreatedEvent notification,
        CancellationToken cancellationToken) =>
        hubContext.Clients.User(notification.UserId.ToString())
            .SendAsync("OrderCreated", notification, cancellationToken);

    public Task DispatchRefundStatusChangedAsync(
        RefundStatusChangedEvent statusEvent,
        CancellationToken cancellationToken) =>
        hubContext.Clients.User(statusEvent.UserId.ToString())
            .SendAsync("RefundStatusChanged", statusEvent, cancellationToken);
}
