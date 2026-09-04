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
    internal const string RetryHeader = "x-showtime-retry-count";

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

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += async (sender, delivery) =>
                {
                    using var scope = scopeFactory.CreateScope();
                    var handler = scope.ServiceProvider.GetRequiredService<OrderNotificationMessageHandler>();
                    var result = await handler.HandleAsync(
                        delivery.BasicProperties.Type,
                        delivery.Body,
                        delivery.CancellationToken);
                    if (result == OrderNotificationHandlingResult.Acknowledge)
                    {
                        await channel.BasicAckAsync(delivery.DeliveryTag, false, delivery.CancellationToken);
                    }
                    else if (result == OrderNotificationHandlingResult.DeadLetter)
                    {
                        await channel.BasicNackAsync(delivery.DeliveryTag, false, false, delivery.CancellationToken);
                    }
                    else
                    {
                        var retryCount = ReadRetryCount(delivery.BasicProperties.Headers);
                        if (ShouldRetry(retryCount, settings.ConsumerMaxRetries))
                        {
                            await Task.Delay(
                                TimeSpan.FromMilliseconds(Math.Min(100 * Math.Pow(2, retryCount), 5000)),
                                delivery.CancellationToken);
                            var headers = delivery.BasicProperties.Headers is null
                                ? new Dictionary<string, object?>()
                                : new Dictionary<string, object?>(delivery.BasicProperties.Headers);
                            headers[RetryHeader] = retryCount + 1;
                            var properties = new BasicProperties
                            {
                                Persistent = true,
                                ContentType = delivery.BasicProperties.ContentType,
                                MessageId = delivery.BasicProperties.MessageId,
                                Type = delivery.BasicProperties.Type,
                                Headers = headers,
                            };
                            try
                            {
                                await channel.BasicPublishAsync(
                                    settings.ExchangeName,
                                    delivery.RoutingKey,
                                    mandatory: true,
                                    properties,
                                    delivery.Body,
                                    delivery.CancellationToken);
                                await channel.BasicAckAsync(
                                    delivery.DeliveryTag, false, delivery.CancellationToken);
                            }
                            catch
                            {
                                await channel.BasicNackAsync(
                                    delivery.DeliveryTag, false, true, delivery.CancellationToken);
                                throw;
                            }
                        }
                        else
                        {
                            await channel.BasicNackAsync(
                                delivery.DeliveryTag, false, false, delivery.CancellationToken);
                        }
                    }
                };

                await channel.BasicConsumeAsync(
                    settings.OrderNotificationQueueName,
                    autoAck: false,
                    consumer,
                    stoppingToken);
                await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "RabbitMQ order notification consumer failed; connection will be retried.");
                await Task.Delay(TimeSpan.FromSeconds(settings.OutboxPollIntervalSeconds), stoppingToken);
            }
            finally
            {
                if (channel is not null)
                {
                    await channel.DisposeAsync();
                }
            }
        }
    }

    internal static int ReadRetryCount(IDictionary<string, object?>? headers)
    {
        if (headers is null || !headers.TryGetValue(RetryHeader, out var value))
        {
            return 0;
        }

        return value switch
        {
            byte number => number,
            short number => number,
            int number => number,
            long number when number <= int.MaxValue => (int)number,
            _ => 0,
        };
    }

    internal static bool ShouldRetry(int retryCount, int maximumRetries) =>
        retryCount < maximumRetries;
}
