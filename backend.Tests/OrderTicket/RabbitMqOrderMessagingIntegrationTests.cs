using System.Text;
using RabbitMQ.Client;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class RabbitMqOrderMessagingIntegrationTests
{
    [RabbitMqFact]
    public async Task ConfirmedPersistentMessageRoutesAndCanBeManuallyAcknowledged()
    {
        var raw = Environment.GetEnvironmentVariable("SHOWTIME_RABBITMQ_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException(
                "SHOWTIME_RUN_RABBITMQ_TESTS=1 requires SHOWTIME_RABBITMQ_TEST_CONNECTION.");
        }

        var suffix = Guid.NewGuid().ToString("N");
        var exchange = $"showtime.tests.order.{suffix}";
        var queue = $"showtime.tests.order.{suffix}";
        var factory = new ConnectionFactory
        {
            Uri = new Uri(raw),
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
        };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync(new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true));

        try
        {
            await channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, autoDelete: false);
            await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false);
            await channel.QueueBindAsync(queue, exchange, "order.created.v1");
            var eventId = Guid.NewGuid().ToString("D");
            var properties = new BasicProperties
            {
                ContentType = "application/json",
                MessageId = eventId,
                Type = "OrderCreated.v1",
                Persistent = true,
            };

            await channel.BasicPublishAsync(
                exchange,
                "order.created.v1",
                mandatory: true,
                properties,
                Encoding.UTF8.GetBytes("{\"eventType\":\"OrderCreated.v1\"}"));
            var delivery = await channel.BasicGetAsync(queue, autoAck: false);

            Assert.NotNull(delivery);
            Assert.True(delivery!.BasicProperties.Persistent);
            Assert.Equal(eventId, delivery.BasicProperties.MessageId);
            Assert.Equal("OrderCreated.v1", delivery.BasicProperties.Type);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false);
        }
        finally
        {
            await channel.QueueDeleteAsync(queue, ifUnused: false, ifEmpty: false);
            await channel.ExchangeDeleteAsync(exchange, ifUnused: false);
        }
    }

    private sealed class RabbitMqFactAttribute : FactAttribute
    {
        public RabbitMqFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("SHOWTIME_RUN_RABBITMQ_TESTS") != "1")
            {
                Skip = "SHOWTIME_RUN_RABBITMQ_TESTS is not 1; no RabbitMQ connection will be opened.";
            }
        }
    }
}
