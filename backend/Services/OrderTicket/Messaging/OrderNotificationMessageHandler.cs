using System.Text.Json;

namespace ShowtimeBackend.Services.OrderTicket.Messaging;

public enum OrderNotificationHandlingResult
{
    Acknowledge,
    DeadLetter,
    Retry,
}

public interface IOrderNotificationMessageHandler
{
    Task<OrderNotificationHandlingResult> HandleAsync(
        string? messageType,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken);
}

public sealed class OrderNotificationMessageHandler(
    IOrderNotificationDispatcher dispatcher,
    ILogger<OrderNotificationMessageHandler> logger) : IOrderNotificationMessageHandler
{
    public async Task<OrderNotificationHandlingResult> HandleAsync(
        string? messageType,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(messageType, OrderCreatedEvent.TypeName, StringComparison.Ordinal))
        {
            return OrderNotificationHandlingResult.DeadLetter;
        }

        OrderCreatedEvent? notification;
        try
        {
            notification = JsonSerializer.Deserialize<OrderCreatedEvent>(
                body.Span,
                OrderCreatedEvent.SerializerOptions);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "A malformed order notification was rejected.");
            return OrderNotificationHandlingResult.DeadLetter;
        }

        if (notification is null ||
            notification.EventType != OrderCreatedEvent.TypeName ||
            string.IsNullOrWhiteSpace(notification.EventId) ||
            notification.OrderId <= 0 || notification.UserId <= 0)
        {
            return OrderNotificationHandlingResult.DeadLetter;
        }

        try
        {
            await dispatcher.DispatchOrderCreatedAsync(notification, cancellationToken);
            return OrderNotificationHandlingResult.Acknowledge;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Dispatching order notification {EventId} failed.", notification.EventId);
            return OrderNotificationHandlingResult.Retry;
        }
    }
}
