using Microsoft.Extensions.Options;

namespace ShowtimeBackend.Services.OrderTicket.Messaging;

internal interface IOrderNotificationDelivery
{
    string? MessageType { get; }
    ReadOnlyMemory<byte> Body { get; }
    IDictionary<string, object?>? Headers { get; }
    bool IsChannelOpen { get; }
    Task AcknowledgeAsync(CancellationToken cancellationToken);
    Task DeadLetterAsync(CancellationToken cancellationToken);
    Task PublishRetryAsync(int retryCount, CancellationToken cancellationToken);
}

internal enum OrderNotificationDeliveryOutcome
{
    Completed,
    ChannelUnavailable,
}

internal sealed class OrderNotificationDeliveryProcessor(
    IOrderNotificationMessageHandler handler,
    IOptions<RabbitMqOptions> options,
    ILogger<OrderNotificationDeliveryProcessor> logger)
{
    internal const string RetryHeader = "x-showtime-retry-count";

    public async Task<OrderNotificationDeliveryOutcome> ProcessAsync(
        IOrderNotificationDelivery delivery,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!TryReadRetryCount(delivery.Headers, out var retryCount))
            {
                logger.LogWarning("An order notification with an invalid retry header was dead-lettered.");
                return await TryDeadLetterAsync(delivery, cancellationToken);
            }

            var handling = await handler.HandleAsync(
                delivery.MessageType,
                delivery.Body,
                cancellationToken);
            if (handling == OrderNotificationHandlingResult.Acknowledge)
            {
                await delivery.AcknowledgeAsync(cancellationToken);
                return OrderNotificationDeliveryOutcome.Completed;
            }

            if (handling == OrderNotificationHandlingResult.DeadLetter ||
                retryCount >= options.Value.ConsumerMaxRetries)
            {
                return await TryDeadLetterAsync(delivery, cancellationToken);
            }

            await Task.Delay(
                TimeSpan.FromMilliseconds(Math.Min(100 * Math.Pow(2, retryCount), 5000)),
                cancellationToken);
            try
            {
                // PublishRetryAsync only returns after publisher confirmation. The original
                // delivery is acknowledged strictly after this call succeeds.
                await delivery.PublishRetryAsync(retryCount + 1, cancellationToken);
                await delivery.AcknowledgeAsync(cancellationToken);
                return OrderNotificationDeliveryOutcome.Completed;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Republishing an order notification retry failed; the original delivery will be dead-lettered.");
                return await TryDeadLetterAsync(delivery, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unexpected order notification delivery failure; the message will be dead-lettered.");
            return await TryDeadLetterAsync(delivery, cancellationToken);
        }
    }

    internal static bool TryReadRetryCount(
        IDictionary<string, object?>? headers,
        out int retryCount)
    {
        retryCount = 0;
        if (headers is null || !headers.TryGetValue(RetryHeader, out var value))
        {
            return true;
        }

        switch (value)
        {
            case byte number:
                retryCount = number;
                return true;
            case short number when number >= 0:
                retryCount = number;
                return true;
            case int number when number >= 0:
                retryCount = number;
                return true;
            case long number when number is >= 0 and <= int.MaxValue:
                retryCount = (int)number;
                return true;
            default:
                return false;
        }
    }

    private async Task<OrderNotificationDeliveryOutcome> TryDeadLetterAsync(
        IOrderNotificationDelivery delivery,
        CancellationToken cancellationToken)
    {
        if (!delivery.IsChannelOpen)
        {
            logger.LogWarning("The RabbitMQ channel closed before the delivery could be dead-lettered; broker recovery will requeue it.");
            return OrderNotificationDeliveryOutcome.ChannelUnavailable;
        }

        try
        {
            await delivery.DeadLetterAsync(cancellationToken);
            return OrderNotificationDeliveryOutcome.Completed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The RabbitMQ channel could not dead-letter a delivery; the consumer will be rebuilt.");
            return OrderNotificationDeliveryOutcome.ChannelUnavailable;
        }
    }
}
