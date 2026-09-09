using System.Text;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using ShowtimeBackend.Entities.OrderTicket;

namespace ShowtimeBackend.Services.OrderTicket.Messaging;

public sealed class RabbitMqOrderEventPublisher(
    IRabbitMqConnectionProvider connectionProvider,
    IOptions<RabbitMqOptions> options) : IOrderEventPublisher, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly RabbitMqOptions _options = options.Value;
    private IChannel? _channel;

    public async Task PublishAsync(OrderEventOutbox message, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_channel is not { IsOpen: true })
            {
                if (_channel is not null)
                {
                    await _channel.DisposeAsync();
                }

                var connection = await connectionProvider.GetConnectionAsync(cancellationToken);
                _channel = await connection.CreateChannelAsync(new CreateChannelOptions(
                    publisherConfirmationsEnabled: true,
                    publisherConfirmationTrackingEnabled: true), cancellationToken);
                await RabbitMqTopology.DeclareAsync(_channel, _options, cancellationToken);
            }

            var properties = new BasicProperties
            {
                Persistent = true,
                ContentType = "application/json",
                MessageId = message.EventId,
                Type = message.EventType,
            };
            await _channel.BasicPublishAsync(
                _options.ExchangeName,
                message.RoutingKey,
                mandatory: true,
                properties,
                Encoding.UTF8.GetBytes(message.Payload),
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        _gate.Dispose();
    }
}
