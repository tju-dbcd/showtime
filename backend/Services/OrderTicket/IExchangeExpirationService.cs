namespace ShowtimeBackend.Services.OrderTicket;

public interface IExchangeExpirationService
{
    Task<ExchangeExpirationBatchResult> ExpireDueBatchAsync(
        long? afterExchangeId = null,
        CancellationToken cancellationToken = default);
}

public sealed record ExchangeExpirationBatchResult(
    int CandidateCount,
    int SuccessCount,
    int FailureCount,
    long? LastExchangeId);
