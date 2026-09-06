using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace ShowtimeBackend.Services.OrderTicket.Messaging;

public sealed class RabbitMqOrderNotificationWorker(
    IRabbitMqConnectionProvider connectionProvider,
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqOrderNotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        while (!stoppingToken.IsCancellationRequested)
        {
            IChannel? channel = null;
            try
            {
                var connection = await connectionProvider.GetConnectionAsync(stoppingToken);
                channel = await connection.CreateChannelAsync(new CreateChannelOptions(
                    publisherConfirmationsEnabled: true,
                    publisherConfirmationTrackingEnabled: true), stoppingToken);
                await RabbitMqTopology.DeclareAsync(channel, settings, stoppingToken);
                await channel.BasicQosAsync(0, settings.PrefetchCount, false, stoppingToken);

                var lifetime = new RabbitMqConsumerLifetime();
                var consumer = new AsyncEventingBasicConsumer(channel);
                var activeChannel = channel;
                consumer.ReceivedAsync += async (_, eventArgs) =>
                {
                    var delivery = new RabbitMqOrderNotificationDelivery(
                        activeChannel,
                        settings,
                        eventArgs);
                    OrderNotificationDeliveryOutcome outcome;
                    try
                    {
                        using var scope = scopeFactory.CreateScope();
                        var processor = scope.ServiceProvider
                            .GetRequiredService<OrderNotificationDeliveryProcessor>();
                        outcome = await processor.ProcessAsync(delivery, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception exception)
                    {
                        // RabbitMQ.Client reports callback exceptions through CallbackExceptionAsync
                        // instead of propagating them to ExecuteAsync. Contain every application
                        // failure here and give the delivery a terminal disposition when possible.
                        logger.LogError(exception, "Unhandled order notification callback failure.");
                        outcome = await TryDeadLetterUnexpectedAsync(delivery, stoppingToken);
                    }

                    if (outcome == OrderNotificationDeliveryOutcome.ChannelUnavailable)
                    {
                        lifetime.SignalEnded();
                    }
                };
                consumer.ShutdownAsync += (_, _) =>
                {
                    lifetime.SignalEnded();
                    return Task.CompletedTask;
                };
                consumer.UnregisteredAsync += (_, _) =>
                {
                    lifetime.SignalEnded();
                    return Task.CompletedTask;
                };
                activeChannel.ChannelShutdownAsync += (_, eventArgs) =>
                {
                    logger.LogWarning("RabbitMQ notification channel shut down: {ReplyText}", eventArgs.ReplyText);
                    lifetime.SignalEnded();
                    return Task.CompletedTask;
                };
                activeChannel.CallbackExceptionAsync += (_, eventArgs) =>
                {
                    logger.LogError(eventArgs.Exception, "RabbitMQ notification channel callback failed; the consumer will be rebuilt.");
                    lifetime.SignalEnded();
                    return Task.CompletedTask;
                };

                await activeChannel.BasicConsumeAsync(
                    settings.OrderNotificationQueueName,
                    autoAck: false,
                    consumer,
                    stoppingToken);
                await lifetime.WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "RabbitMQ order notification consumer failed; connection will be retried.");
            }
            finally
            {
                if (channel is not null)
                {
                    try
                    {
                        await channel.DisposeAsync();
                    }
                    catch (Exception exception)
                    {
                        logger.LogWarning(exception, "Disposing a RabbitMQ notification channel failed; a fresh channel will still be created.");
                    }
                }
            }

            if (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(settings.OutboxPollIntervalSeconds),
                    stoppingToken);
            }
        }
    }

    private async Task<OrderNotificationDeliveryOutcome> TryDeadLetterUnexpectedAsync(
        IOrderNotificationDelivery delivery,
        CancellationToken cancellationToken)
    {
        if (!delivery.IsChannelOpen)
        {
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
            logger.LogWarning(exception, "Unable to dead-letter the delivery after an unexpected callback failure.");
            return OrderNotificationDeliveryOutcome.ChannelUnavailable;
        }
    }

    private sealed class RabbitMqOrderNotificationDelivery(
        IChannel channel,
        RabbitMqOptions settings,
        BasicDeliverEventArgs eventArgs) : IOrderNotificationDelivery
    {
        public string? MessageType => eventArgs.BasicProperties.Type;
        public ReadOnlyMemory<byte> Body => eventArgs.Body;
        public IDictionary<string, object?>? Headers => eventArgs.BasicProperties.Headers;
        public bool IsChannelOpen => channel.IsOpen;

        public Task AcknowledgeAsync(CancellationToken cancellationToken) =>
            channel.BasicAckAsync(eventArgs.DeliveryTag, false, cancellationToken).AsTask();

        public Task DeadLetterAsync(CancellationToken cancellationToken) =>
            channel.BasicNackAsync(eventArgs.DeliveryTag, false, false, cancellationToken).AsTask();

        public async Task PublishRetryAsync(int retryCount, CancellationToken cancellationToken)
        {
            var headers = eventArgs.BasicProperties.Headers is null
                ? new Dictionary<string, object?>()
                : new Dictionary<string, object?>(eventArgs.BasicProperties.Headers);
            headers[OrderNotificationDeliveryProcessor.RetryHeader] = retryCount;
            var properties = new BasicProperties
            {
                Persistent = true,
                ContentType = eventArgs.BasicProperties.ContentType,
                MessageId = eventArgs.BasicProperties.MessageId,
                Type = eventArgs.BasicProperties.Type,
                Headers = headers,
            };
            await channel.BasicPublishAsync(
                settings.ExchangeName,
                eventArgs.RoutingKey,
                mandatory: true,
                properties,
                eventArgs.Body,
                cancellationToken);
        }
    }
}
