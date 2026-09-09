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
            RefundStatusChangedEvent.TypeName => await HandleRefundStatusChangedAsync(body, cancellationToken),
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
            // 先推送“审核通过（处理中）”，再执行退款完成；若推送失败仅记录，
            // 不阻断退款完成（完成后另有 RefundStatusChanged(COMPLETED) 通知）。
            await TryDispatchApprovedStatusAsync(approvedEvent, cancellationToken);
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

    private async Task<OrderNotificationHandlingResult> HandleRefundStatusChangedAsync(
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken)
    {
        RefundStatusChangedEvent? statusEvent;
        try
        {
            statusEvent = JsonSerializer.Deserialize<RefundStatusChangedEvent>(
                body.Span,
                OrderCreatedEvent.SerializerOptions);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "A malformed refund status notification was rejected.");
            return OrderNotificationHandlingResult.DeadLetter;
        }

        if (statusEvent is null ||
            statusEvent.EventType != RefundStatusChangedEvent.TypeName ||
            string.IsNullOrWhiteSpace(statusEvent.EventId) ||
            statusEvent.RefundId <= 0 || statusEvent.OrderId <= 0 || statusEvent.UserId <= 0 ||
            string.IsNullOrWhiteSpace(statusEvent.ApproveStatus) ||
            string.IsNullOrWhiteSpace(statusEvent.RefundStatus))
        {
            return OrderNotificationHandlingResult.DeadLetter;
        }

        try
        {
            await dispatcher.DispatchRefundStatusChangedAsync(
                statusEvent,
                cancellationToken);
            return OrderNotificationHandlingResult.Acknowledge;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Dispatching refund status notification {EventId} failed.",
                statusEvent.EventId);
            return OrderNotificationHandlingResult.Retry;
        }
    }

    private async Task TryDispatchApprovedStatusAsync(
        RefundApprovedEvent approvedEvent,
        CancellationToken cancellationToken)
    {
        var processingStatus = new RefundStatusChangedEvent(
            Guid.NewGuid().ToString("D"),
            RefundStatusChangedEvent.TypeName,
            approvedEvent.OccurredAt,
            approvedEvent.RefundId,
            approvedEvent.RefundNo,
            approvedEvent.OrderId,
            approvedEvent.UserId,
            "APPROVED",
            "PROCESSING",
            approvedEvent.ActualRefund);
        try
        {
            await dispatcher.DispatchRefundStatusChangedAsync(
                processingStatus,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Dispatching refund approved status for refund {RefundId} failed; completion continues.",
                approvedEvent.RefundId);
        }
    }
}
