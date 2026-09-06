using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ShowtimeBackend.Data;
using ShowtimeBackend.Entities.OrderTicket;

namespace ShowtimeBackend.Services.OrderTicket.Messaging;

public sealed record OutboxBatchResult(int Claimed, int Published, int Retried, int Failed);

public interface IOrderEventPublisher
{
    Task PublishAsync(OrderEventOutbox message, CancellationToken cancellationToken);
}

public interface IOrderEventOutboxService
{
    Task<OutboxBatchResult> ProcessBatchAsync(CancellationToken cancellationToken);
}

public sealed class OrderEventOutboxService(
    IDbContextFactory<AppDbContext> contextFactory,
    IOrderEventPublisher publisher,
    TimeProvider timeProvider,
    IOptions<RabbitMqOptions> options,
    ILogger<OrderEventOutboxService> logger) : IOrderEventOutboxService
{
    private readonly RabbitMqOptions _options = options.Value;
    private readonly string _workerId = $"{Environment.ProcessId}:{Guid.NewGuid():N}";

    public async Task<OutboxBatchResult> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        var messages = await ClaimAsync(cancellationToken);
        var published = 0;
        var retried = 0;
        var failed = 0;

        foreach (var message in messages)
        {
            try
            {
                await publisher.PublishAsync(message, cancellationToken);
                await MarkPublishedAsync(message.EventId, cancellationToken);
                published++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                var terminal = message.AttemptCount >= _options.MaxPublishAttempts;
                await MarkFailedAttemptAsync(message.EventId, message.AttemptCount, exception, terminal, cancellationToken);
                if (terminal)
                {
                    failed++;
                }
                else
                {
                    retried++;
                }

                logger.LogWarning(exception, "Publishing outbox event {EventId} failed on attempt {Attempt}.", message.EventId, message.AttemptCount);
            }
        }

        return new OutboxBatchResult(messages.Count, published, retried, failed);
    }

    private async Task<List<OrderEventOutbox>> ClaimAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var leaseUntil = now.AddSeconds(_options.ProcessingLeaseSeconds);
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        var candidates = await dbContext.OrderEventOutbox
            .AsNoTracking()
            .Where(item =>
                (item.Status == "PENDING" && item.NextAttemptAt <= now) ||
                (item.Status == "PROCESSING" && item.LockedUntil <= now))
            .OrderBy(item => item.NextAttemptAt)
            .ThenBy(item => item.EventId)
            .Select(item => item.EventId)
            .Take(_options.PublishBatchSize)
            .ToListAsync(cancellationToken);

        foreach (var eventId in candidates)
        {
            await dbContext.OrderEventOutbox
                .Where(item => item.EventId == eventId &&
                    ((item.Status == "PENDING" && item.NextAttemptAt <= now) ||
                     (item.Status == "PROCESSING" && item.LockedUntil <= now)))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.Status, "PROCESSING")
                    .SetProperty(item => item.LockOwner, _workerId)
                    .SetProperty(item => item.LockedUntil, leaseUntil)
                    .SetProperty(item => item.AttemptCount, item => item.AttemptCount + 1)
                    .SetProperty(item => item.UpdateTime, now), cancellationToken);
        }

        return await dbContext.OrderEventOutbox
            .AsNoTracking()
            .Where(item => item.LockOwner == _workerId && item.Status == "PROCESSING" && item.LockedUntil == leaseUntil)
            .OrderBy(item => item.EventId)
            .ToListAsync(cancellationToken);
    }

    private async Task MarkPublishedAsync(string eventId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.OrderEventOutbox
            .Where(item => item.EventId == eventId && item.Status == "PROCESSING" && item.LockOwner == _workerId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, "PUBLISHED")
                .SetProperty(item => item.PublishedAt, now)
                .SetProperty(item => item.LockOwner, (string?)null)
                .SetProperty(item => item.LockedUntil, (DateTime?)null)
                .SetProperty(item => item.LastError, (string?)null)
                .SetProperty(item => item.UpdateTime, now), cancellationToken);
    }

    private async Task MarkFailedAttemptAsync(
        string eventId,
        int attemptCount,
        Exception exception,
        bool terminal,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var exponent = Math.Min(attemptCount - 1, 30);
        var backoff = Math.Min(Math.Pow(2, exponent), _options.MaxBackoffSeconds);
        var nextAttemptAt = now.AddSeconds(backoff);
        var error = exception.Message.Length <= 1000 ? exception.Message : exception.Message[..1000];
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.OrderEventOutbox
            .Where(item => item.EventId == eventId && item.Status == "PROCESSING" && item.LockOwner == _workerId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, terminal ? "FAILED" : "PENDING")
                .SetProperty(item => item.NextAttemptAt, nextAttemptAt)
                .SetProperty(item => item.LockOwner, (string?)null)
                .SetProperty(item => item.LockedUntil, (DateTime?)null)
                .SetProperty(item => item.LastError, error)
                .SetProperty(item => item.UpdateTime, now), cancellationToken);
    }
}
