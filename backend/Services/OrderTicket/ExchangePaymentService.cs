using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Common;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Entities.SeatZone;

namespace ShowtimeBackend.Services.OrderTicket;

public sealed class ExchangePaymentService(
    AppDbContext dbContext,
    TimeProvider timeProvider,
    IExchangeLockCoordinator lockCoordinator,
    IExchangeApplicationService applicationService,
    IExchangeReviewService reviewService,
    ITicketIssuanceService ticketIssuanceService) : IExchangePaymentService
{
    public async Task<OrderTicketResult<ExchangePaymentResponse>> PayAsync(
        long userId,
        string actor,
        long exchangeId,
        ExchangePaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var result = await PayOnceAsync(
                userId, actor, exchangeId, request, cancellationToken);
            if (attempt == 0 && result.ErrorCode == "EXCHANGE_TICKET_IDENTIFIER_COLLISION")
                continue;
            return result.ErrorCode == "EXCHANGE_TICKET_IDENTIFIER_COLLISION"
                ? Conflict("EXCHANGE_PAYMENT_CONFLICT", "Generated ticket identifiers collided twice.")
                : result;
        }
        return Conflict("EXCHANGE_PAYMENT_CONFLICT", "Exchange payment could not be completed.");
    }

    private async Task<OrderTicketResult<ExchangePaymentResponse>> PayOnceAsync(
        long userId,
        string actor,
        long exchangeId,
        ExchangePaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await lockCoordinator.LockExchangeRequestAsync(exchangeId, cancellationToken))
        {
            await RollbackAndClearAsync(transaction, cancellationToken);
            return NotFound("EXCHANGE_NOT_FOUND", "The exchange request does not exist.");
        }
        dbContext.ChangeTracker.Clear();
        var exchange = await dbContext.Set<ExchangeRequest>()
            .Include(item => item.Items).ThenInclude(item => item.OrderItem)!.ThenInclude(item => item!.ETicket)
            .Include(item => item.Items).ThenInclude(item => item.NewOrderItem)!
                .ThenInclude(item => item!.ETicket)
            .SingleOrDefaultAsync(item => item.ExchangeId == exchangeId && item.UserId == userId,
                cancellationToken);
        if (exchange is null)
        {
            await RollbackAndClearAsync(transaction, cancellationToken);
            return NotFound("EXCHANGE_NOT_FOUND", "The exchange request does not exist.");
        }
        var childIds = exchange.Items.Where(item => item.NewOrderItem is not null)
            .Select(item => item.NewOrderItem!.OrderId).Distinct().ToArray();
        if (!await lockCoordinator.LockOrderAsync(exchange.OrderId, cancellationToken) ||
            childIds.Length != 1 || !await lockCoordinator.LockOrderAsync(childIds[0], cancellationToken))
        {
            await RollbackAndClearAsync(transaction, cancellationToken);
            return Conflict("EXCHANGE_DATA_INCONSISTENT", "The exchange child order is inconsistent.");
        }
        var aggregateItemIds = exchange.Items
            .SelectMany(item => new[] { item.OrderItemId, item.NewOrderItemId })
            .ToArray();
        if (!await ExchangeLockProtocol.LockItemsTicketsAndReservationsAsync(
                dbContext,
                lockCoordinator,
                aggregateItemIds,
                cancellationToken))
        {
            await RollbackAndClearAsync(transaction, cancellationToken);
            return Conflict(
                "EXCHANGE_RESOURCE_CONFLICT",
                "An exchange resource changed while the payment was being locked.");
        }
        dbContext.ChangeTracker.Clear();
        exchange = await dbContext.Set<ExchangeRequest>()
            .Include(item => item.Items).ThenInclude(item => item.OrderItem)!
                .ThenInclude(item => item!.ETicket)
            .Include(item => item.Items).ThenInclude(item => item.NewOrderItem)!
                .ThenInclude(item => item!.ETicket)
            .SingleAsync(
                item => item.ExchangeId == exchangeId && item.UserId == userId,
                cancellationToken);
        var child = await dbContext.Set<Order>()
            .Include(item => item.Items).ThenInclude(item => item.ETicket)
            .Include(item => item.Payments)
            .SingleAsync(item => item.OrderId == childIds[0], cancellationToken);
        if (exchange.ApproveStatus == "APPROVED" && exchange.ExchangeStatus == "COMPLETED")
        {
            var existing = await ValidateCompletedAggregateAsync(
                exchange,
                child,
                cancellationToken);
            await RollbackAndClearAsync(transaction, cancellationToken);
            await transaction.DisposeAsync();
            if (existing is null)
                return Conflict(
                    "EXCHANGE_PAYMENT_CONFLICT",
                    "The completed exchange aggregate failed idempotency validation.");
            var completed = await applicationService.GetAsync(userId, exchangeId, cancellationToken);
            return completed.IsSuccess
                ? OrderTicketResult<ExchangePaymentResponse>.Success(
                    new ExchangePaymentResponse(ToResponse(existing), completed.Value!))
                : CopyFailure<ExchangeResponse>(completed);
        }
        if (exchange.ApproveStatus != "APPROVED" || exchange.ExchangeStatus != "PROCESSING" ||
            child.OrderType != "EXCHANGE" || child.OrderStatus != "PENDING_PAY" || child.TotalAmount <= 0m ||
            child.TotalAmount != exchange.PriceDiff + exchange.ExchangeFee)
        {
            await RollbackAndClearAsync(transaction, cancellationToken);
            return Conflict("EXCHANGE_PAYMENT_CONFLICT", "The exchange is not awaiting payment.");
        }
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (child.ExpireTime <= now)
        {
            await RollbackAndClearAsync(transaction, cancellationToken);
            await transaction.DisposeAsync();
            var expiration = await reviewService.ExpireAsync(
                exchangeId, "exchange-expiration", cancellationToken);
            if (!expiration.IsSuccess)
                return CopyFailure<ExchangeResponse>(expiration);
            return Conflict("EXCHANGE_PAYMENT_EXPIRED", "The exchange payment period has expired.");
        }

        var status = request.Result.ToString();
        var payment = ExchangeReviewService.CreatePayment(
            child, userId, actor, now, request.PayChannel.ToString(), status, child.TotalAmount);
        child.Payments.Add(payment);
        if (request.Result == PaymentResult.SUCCESS)
        {
            var newIds = exchange.Items.Select(item => item.NewOrderItemId).ToArray();
            var targetReservations = await dbContext.Set<SeatReservation>()
                .Where(item => item.OrderItemId.HasValue && newIds.Contains(item.OrderItemId.Value))
                .ToListAsync(cancellationToken);
            var originalIds = exchange.Items.Select(item => item.OrderItemId).ToArray();
            var originalReservations = await dbContext.Set<SeatReservation>()
                .Where(item => item.OrderItemId.HasValue && originalIds.Contains(item.OrderItemId.Value))
                .ToListAsync(cancellationToken);
            if (exchange.Items.Any(item => item.OrderItem?.ETicket is null ||
                                           item.OrderItem.ItemStatus != "EXCHANGING" ||
                                           item.OrderItem.ETicket.TicketStatus != "EXCHANGING") ||
                targetReservations.Count != newIds.Length ||
                targetReservations.Any(item => item.ReservationStatus != "ACTIVE") ||
                originalReservations.Count != originalIds.Length ||
                originalReservations.Any(item => item.ReservationStatus != "ACTIVE"))
            {
                await RollbackAndClearAsync(transaction, cancellationToken);
                return Conflict("EXCHANGE_PAYMENT_CONFLICT", "Exchange resources changed before payment.");
            }
            child.PayTime = now;
            child.UpdateBy = actor;
            var issuance = ticketIssuanceService.Issue(
                child, TicketIssuanceContext.Exchange, actor,
                new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc)));
            if (!issuance.IsSuccess)
            {
                await RollbackAndClearAsync(transaction, cancellationToken);
                return OrderTicketResult<ExchangePaymentResponse>.Fail(
                    issuance.Failure, issuance.ErrorCode!, issuance.Message!);
            }
            foreach (var item in exchange.Items)
            {
                item.OrderItem!.ItemStatus = "EXCHANGED";
                item.OrderItem.ETicket!.TicketStatus = "EXCHANGED";
                item.OrderItem.UpdateBy = actor;
                item.OrderItem.ETicket.UpdateBy = actor;
            }
            foreach (var reservation in originalReservations)
            {
                reservation.ReservationStatus = "RELEASED";
                reservation.CancelTime ??= now;
                reservation.UpdateBy = actor;
            }
            exchange.ExchangeStatus = "COMPLETED";
            exchange.CompleteTime = now;
            exchange.UpdateBy = actor;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            await RollbackAndClearAsync(transaction, CancellationToken.None);
            if (TicketConstraintClassifier.Classify(exception) is
                TicketUniqueConstraint.TicketNumber or
                TicketUniqueConstraint.QrCode or
                TicketUniqueConstraint.AntiFakeCode)
            {
                return Conflict("EXCHANGE_TICKET_IDENTIFIER_COLLISION",
                    "A generated ticket identifier collided and will be retried.");
            }
            return Conflict("EXCHANGE_PAYMENT_CONFLICT", "Exchange payment conflicted with another operation.");
        }
        dbContext.ChangeTracker.Clear();
        var detail = await applicationService.GetAsync(userId, exchangeId, cancellationToken);
        return detail.IsSuccess
            ? OrderTicketResult<ExchangePaymentResponse>.Success(
                new ExchangePaymentResponse(ToResponse(payment), detail.Value!))
            : CopyFailure<ExchangeResponse>(detail);
    }

    private async Task<Payment?> ValidateCompletedAggregateAsync(
        ExchangeRequest exchange,
        Order child,
        CancellationToken cancellationToken)
    {
        var matchingPayments = child.Payments
            .Where(item => item.PayStatus == "SUCCESS" &&
                           item.PayAmount == child.TotalAmount)
            .OrderByDescending(item => item.PaymentId)
            .ToList();
        var originalIds = exchange.Items.Select(item => item.OrderItemId).ToArray();
        var newIds = exchange.Items.Select(item => item.NewOrderItemId).ToArray();
        var reservations = await dbContext.Set<SeatReservation>()
            .AsNoTracking()
            .Where(item => item.OrderItemId.HasValue &&
                           (originalIds.Contains(item.OrderItemId.Value) ||
                            newIds.Contains(item.OrderItemId.Value)))
            .ToListAsync(cancellationToken);
        var originalReservations = reservations
            .Where(item => item.OrderItemId.HasValue &&
                           originalIds.Contains(item.OrderItemId.Value))
            .ToList();
        var targetReservations = reservations
            .Where(item => item.OrderItemId.HasValue &&
                           newIds.Contains(item.OrderItemId.Value))
            .ToList();

        if (matchingPayments.Count != 1 ||
            child.OrderType != "EXCHANGE" ||
            child.ParentOrderId != exchange.OrderId ||
            child.OrderStatus != "ISSUED" ||
            child.TotalAmount <= 0m ||
            child.TotalAmount != exchange.PriceDiff + exchange.ExchangeFee ||
            child.Items.Count != newIds.Length ||
            exchange.Items.Any(item =>
                item.OrderItem?.ItemStatus != "EXCHANGED" ||
                item.OrderItem.ETicket?.TicketStatus != "EXCHANGED" ||
                item.NewOrderItem?.ETicket?.TicketStatus != "UNUSED") ||
            originalReservations.Count != originalIds.Length ||
            originalReservations.Any(item => item.ReservationStatus != "RELEASED") ||
            targetReservations.Count != newIds.Length ||
            targetReservations.Any(item => item.ReservationStatus != "ACTIVE"))
        {
            return null;
        }

        return matchingPayments[0];
    }

    private async Task RollbackAndClearAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
    }

    private static PaymentResponse ToResponse(Payment payment) => new(
        payment.PaymentId, payment.PaymentNo, payment.OrderId, payment.PayAmount,
        payment.PayChannel.ToEnum<PaymentChannel>(), payment.PayStatus.ToEnum<PaymentStatus>(),
        payment.TradeNo, payment.CallbackTime, payment.PayTime);
    private static OrderTicketResult<ExchangePaymentResponse> CopyFailure<T>(OrderTicketResult<T> source) =>
        OrderTicketResult<ExchangePaymentResponse>.Fail(source.Failure, source.ErrorCode!, source.Message!);
    private static OrderTicketResult<ExchangePaymentResponse> NotFound(string code, string message) =>
        OrderTicketResult<ExchangePaymentResponse>.Fail(OrderTicketFailure.NotFound, code, message);
    private static OrderTicketResult<ExchangePaymentResponse> Conflict(string code, string message) =>
        OrderTicketResult<ExchangePaymentResponse>.Fail(OrderTicketFailure.Conflict, code, message);
}
