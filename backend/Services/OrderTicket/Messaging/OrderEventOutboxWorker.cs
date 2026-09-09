using Microsoft.Extensions.Options;

namespace ShowtimeBackend.Services.OrderTicket.Messaging;

public sealed class OrderEventOutboxWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> options,
    ILogger<OrderEventOutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(options.Value.OutboxPollIntervalSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IOrderEventOutboxService>();
                OutboxBatchResult result;
                do
                {
                    result = await service.ProcessBatchAsync(stoppingToken);
                }
                while (result.Claimed == options.Value.PublishBatchSize && !stoppingToken.IsCancellationRequested);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Order outbox scan failed; the next cycle will retry.");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }
}
