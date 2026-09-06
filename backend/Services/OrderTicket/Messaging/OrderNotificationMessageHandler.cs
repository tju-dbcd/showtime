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
    IRefundCompletionService refundCompletionService,
    ILogger<OrderNotificationMessageHandler> logger) : IOrderNotificationMessageHandler
{
    public async Task<OrderNotificationHandlingResult> HandleAsync(
        string? messageType,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken)
    {
        return messageType switch
        {
            OrderCreatedEvent.TypeName => await HandleOrderCreatedAsync(body, cancellationToken),
            RefundApprovedEvent.TypeName => await HandleRefundApprovedAsync(body, cancellationToken),
            _ => OrderNotificationHandlingResult.DeadLetter,
        };
    }

    private async Task<OrderNotificationHandlingResult> HandleOrderCreatedAsync(
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken)
    {
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

    private async Task<OrderNotificationHandlingResult> HandleRefundApprovedAsync(
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken)
    {
        RefundApprovedEvent? approvedEvent;
        try
        {
            approvedEvent = JsonSerializer.Deserialize<RefundApprovedEvent>(
                body.Span,
                OrderCreatedEvent.SerializerOptions);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "A malformed refund approval event was rejected.");
            return OrderNotificationHandlingResult.DeadLetter;
        }

        if (approvedEvent is null)
        {
            return OrderNotificationHandlingResult.DeadLetter;
        }

        try
        {
            var result = await refundCompletionService.CompleteAsync(
                approvedEvent,
                cancellationToken);
            return result.Outcome switch
            {
                RefundCompletionOutcome.Completed => OrderNotificationHandlingResult.Acknowledge,
                RefundCompletionOutcome.AlreadyCompleted => OrderNotificationHandlingResult.Acknowledge,
                RefundCompletionOutcome.RetryableFailure => OrderNotificationHandlingResult.Retry,
                _ => OrderNotificationHandlingResult.DeadLetter,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Completing refund {RefundId} from event {EventId} failed transiently.",
                approvedEvent.RefundId,
                approvedEvent.EventId);
            return OrderNotificationHandlingResult.Retry;
        }
    }
}
