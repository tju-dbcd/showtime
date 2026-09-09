using Microsoft.Extensions.Options;

namespace ShowtimeBackend.Services.OrderTicket;

public sealed class OrderExpirationWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<OrderExpirationOptions> options,
    ILogger<OrderExpirationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                long? afterOrderId = null;
                do
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var service = scope.ServiceProvider
                        .GetRequiredService<IOrderExpirationService>();
                    var batch = await service.ExpireDueBatchAsync(
                        afterOrderId,
                        stoppingToken);
                    if (batch.CandidateCount < options.Value.ExpirationBatchSize)
                        break;
                    afterOrderId = batch.LastOrderId;
                } while (!stoppingToken.IsCancellationRequested);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Order expiration scan failed; the next scheduled scan will retry.");
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(options.Value.ExpirationScanIntervalSeconds),
                    timeProvider,
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
