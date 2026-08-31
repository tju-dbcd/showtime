using Microsoft.Extensions.Options;

namespace ShowtimeBackend.Services.OrderTicket;

public sealed class ExchangeExpirationWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<ExchangeOptions> options,
    ILogger<ExchangeExpirationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(options.Value.ExpirationScanIntervalSeconds),
                    timeProvider,
                    stoppingToken);
                long? afterExchangeId = null;
                do
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var service = scope.ServiceProvider
                        .GetRequiredService<IExchangeExpirationService>();
                    var batch = await service.ExpireDueBatchAsync(
                        afterExchangeId,
                        stoppingToken);
                    if (batch.CandidateCount < options.Value.ExpirationBatchSize)
                        break;
                    afterExchangeId = batch.LastExchangeId;
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
                    "Exchange expiration scan failed; the next scheduled scan will retry.");
            }
        }
    }
}
