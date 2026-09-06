using RabbitMQ.Client;

namespace ShowtimeBackend.Services.OrderTicket.Messaging;

internal static class RabbitMqTopology
{
    public static async Task DeclareAsync(
        IChannel channel,
        RabbitMqOptions options,
        CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(
            options.ExchangeName, ExchangeType.Topic, true, false, cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(
            options.DeadLetterExchangeName, ExchangeType.Topic, true, false, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(
            options.DeadLetterQueueName, true, false, false, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            options.DeadLetterQueueName,
            options.DeadLetterExchangeName,
            OrderCreatedEvent.RoutingKeyName,
            cancellationToken: cancellationToken);

        var arguments = new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = options.DeadLetterExchangeName,
            ["x-dead-letter-routing-key"] = OrderCreatedEvent.RoutingKeyName,
        };
        await channel.QueueDeclareAsync(
            options.OrderNotificationQueueName, true, false, false, arguments, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            options.OrderNotificationQueueName,
            options.ExchangeName,
            OrderCreatedEvent.RoutingKeyName,
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            options.OrderNotificationQueueName,
            options.ExchangeName,
            RefundApprovedEvent.RoutingKeyName,
            cancellationToken: cancellationToken);
    }
}
