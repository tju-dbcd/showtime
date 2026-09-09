namespace ShowtimeBackend.Services.OrderTicket;

public interface IOrderExpirationService
{
    Task<OrderExpirationBatchResult> ExpireDueBatchAsync(
        long? afterOrderId = null,
        CancellationToken cancellationToken = default);

    Task<OrderExpirationOutcome> ExpireOrderAsync(
        long orderId,
        string actor,
        DateTime now,
        CancellationToken cancellationToken = default);
}

public sealed record OrderExpirationBatchResult(
    int CandidateCount,
    int ExpiredCount,
    int SkippedCount,
    int FailureCount,
    long? LastOrderId);

public enum OrderExpirationOutcome
{
    Expired,
    Skipped,
}
