using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.OrderTicket;

namespace ShowtimeBackend.Services.OrderTicket;

public sealed class ExchangeExpirationService(
    AppDbContext dbContext,
    TimeProvider timeProvider,
    IExchangeReviewService reviewService,
    IOptions<ExchangeOptions> options,
    ILogger<ExchangeExpirationService> logger,
    IServiceScopeFactory? scopeFactory = null) : IExchangeExpirationService
{
    public async Task<ExchangeExpirationBatchResult> ExpireDueBatchAsync(
        long? afterExchangeId = null,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var candidateQuery =
            from exchange in dbContext.Set<ExchangeRequest>().AsNoTracking()
            join relation in dbContext.Set<ExchangeItem>().AsNoTracking()
                on exchange.ExchangeId equals relation.ExchangeId
            join newItem in dbContext.Set<OrderItem>().AsNoTracking()
                on relation.NewOrderItemId equals newItem.OrderItemId
            join child in dbContext.Set<Order>().AsNoTracking()
                on newItem.OrderId equals child.OrderId
            where child.ExpireTime <= now &&
                  (!afterExchangeId.HasValue || exchange.ExchangeId > afterExchangeId.Value) &&
                  ((exchange.ApproveStatus == "PENDING" && exchange.ExchangeStatus == "PENDING") ||
                   (exchange.ApproveStatus == "APPROVED" && exchange.ExchangeStatus == "PROCESSING"))
            select exchange.ExchangeId;
        var candidates = await candidateQuery
            .Distinct()
            .OrderBy(exchangeId => exchangeId)
            .Take(options.Value.ExpirationBatchSize)
            .ToListAsync(cancellationToken);

        var processed = 0;
        foreach (var exchangeId in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                OrderTicketResult<ExchangeResponse> result;
                if (scopeFactory is null)
                {
                    result = await reviewService.ExpireAsync(
                        exchangeId, "exchange-expiration", cancellationToken);
                }
                else
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    result = await scope.ServiceProvider
                        .GetRequiredService<IExchangeReviewService>()
                        .ExpireAsync(exchangeId, "exchange-expiration", cancellationToken);
                }
                if (result.IsSuccess)
                    processed++;
                else
                    logger.LogWarning("Exchange {ExchangeId} expiration skipped: {Code}.",
                        exchangeId, result.ErrorCode);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Exchange {ExchangeId} expiration failed.", exchangeId);
            }
        }
        return new ExchangeExpirationBatchResult(
            candidates.Count,
            processed,
            candidates.Count - processed,
            candidates.Count == 0 ? afterExchangeId : candidates[^1]);
    }
}
