using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ShowtimeBackend.Data;
using ShowtimeBackend.Entities.OrderTicket;

namespace ShowtimeBackend.Services.OrderTicket;

public sealed class OrderExpirationService(
    AppDbContext dbContext,
    TimeProvider timeProvider,
    IOptions<OrderExpirationOptions> options,
    ILogger<OrderExpirationService> logger) : IOrderExpirationService
{
    public const string SystemActor = "order-expiration";

    public async Task<OrderExpirationBatchResult> ExpireDueBatchAsync(
        long? afterOrderId = null,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var candidates = await dbContext.Set<Order>()
            .AsNoTracking()
            .Where(order =>
                order.OrderStatus == "PENDING_PAY" &&
                order.OrderType != "EXCHANGE" &&
                order.ExpireTime <= now &&
                (!afterOrderId.HasValue || order.OrderId > afterOrderId.Value))
            .OrderBy(order => order.OrderId)
            .Select(order => order.OrderId)
            .Take(options.Value.ExpirationBatchSize)
            .ToListAsync(cancellationToken);

        var expired = 0;
        var skipped = 0;
        var failed = 0;
        foreach (var orderId in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var outcome = await ExpireOrderAsync(
                    orderId,
                    SystemActor,
                    now,
                    cancellationToken);
                if (outcome == OrderExpirationOutcome.Expired)
                    expired++;
                else
                    skipped++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failed++;
                dbContext.ChangeTracker.Clear();
                logger.LogError(exception, "Order {OrderId} expiration failed.", orderId);
            }
        }

        return new OrderExpirationBatchResult(
            candidates.Count,
            expired,
            skipped,
            failed,
            candidates.Count == 0 ? afterOrderId : candidates[^1]);
    }

    public async Task<OrderExpirationOutcome> ExpireOrderAsync(
        long orderId,
        string actor,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        dbContext.ChangeTracker.Clear();
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            var order = await dbContext.Set<Order>()
                .Include(item => item.Items)
                .Include(item => item.Payments)
                .SingleOrDefaultAsync(item => item.OrderId == orderId, cancellationToken);
            if (order is null ||
                order.OrderStatus != "PENDING_PAY" ||
                order.OrderType == "EXCHANGE" ||
                order.ExpireTime > now)
            {
                if (transaction is not null)
                    await transaction.RollbackAsync(cancellationToken);
                dbContext.ChangeTracker.Clear();
                return OrderExpirationOutcome.Skipped;
            }

            var orderItemIds = order.Items.Select(item => item.OrderItemId).ToArray();
            var reservations = await dbContext.SeatReservations
                .Where(item =>
                    item.OrderItemId.HasValue &&
                    orderItemIds.Contains(item.OrderItemId.Value) &&
                    item.ReservationType == "ORDER" &&
                    item.ReservationStatus == "ACTIVE")
                .ToListAsync(cancellationToken);

            order.OrderStatus = "CANCELLED";
            order.CancelTime = now;
            order.UpdateBy = actor;
            foreach (var reservation in reservations)
            {
                reservation.ReservationStatus = "CANCELLED";
                reservation.CancelTime = now;
                reservation.UpdateBy = actor;
            }
            foreach (var payment in order.Payments.Where(item => item.PayStatus == "PENDING"))
            {
                payment.PayStatus = "CLOSED";
                payment.UpdateBy = actor;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            return OrderExpirationOutcome.Expired;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            dbContext.ChangeTracker.Clear();
            logger.LogInformation(
                exception,
                "Order {OrderId} expiration lost a concurrent status transition.",
                orderId);
            return OrderExpirationOutcome.Skipped;
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            dbContext.ChangeTracker.Clear();
            throw;
        }
    }
}
