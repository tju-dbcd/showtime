using System.Text;
using ShowtimeBackend.Entities.OrderTicket;

namespace ShowtimeBackend.Services.OrderTicket.Messaging;

// 默认配置（RabbitMq:Enabled=false）下的进程内发布器：不连接 broker，
// 直接把 outbox 消息交给与 RabbitMQ 消费端相同的 OrderNotificationMessageHandler，
// 在进程内完成退款与实时通知，避免默认配置下退款批准后永远停在 PROCESSING。
// 语义映射：Acknowledge → 发布成功；Retry / DeadLetter → 抛异常，
// 交给 OrderEventOutboxService 走指数退避重试，超限后终态 FAILED（与 broker DLQ 语义对齐）。
public sealed class LocalOrderEventPublisher(
    IServiceScopeFactory scopeFactory,
    ILogger<LocalOrderEventPublisher> logger) : IOrderEventPublisher
{
    public async Task PublishAsync(
        OrderEventOutbox message,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<IOrderNotificationMessageHandler>();
        var result = await handler.HandleAsync(
            message.EventType,
            Encoding.UTF8.GetBytes(message.Payload),
            cancellationToken);
        if (result == OrderNotificationHandlingResult.Acknowledge)
        {
            logger.LogDebug(
                "In-process outbox event {EventId} ({EventType}) handled successfully.",
                message.EventId,
                message.EventType);
            return;
        }

        if (result == OrderNotificationHandlingResult.Retry)
        {
            throw new InvalidOperationException(
                $"In-process handling of outbox event {message.EventId} ({message.EventType}) returned a retryable failure.");
        }

        throw new InvalidOperationException(
            $"In-process handling of outbox event {message.EventId} ({message.EventType}) returned a permanent (dead-letter) failure.");
    }
}
