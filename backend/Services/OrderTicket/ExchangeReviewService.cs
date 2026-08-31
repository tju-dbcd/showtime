using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ShowtimeBackend.Common;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Entities.SeatZone;

namespace ShowtimeBackend.Services.OrderTicket;

public sealed class ExchangeReviewService(
    AppDbContext dbContext,
    TimeProvider timeProvider,
    IExchangeLockCoordinator lockCoordinator,
    IExchangeApplicationService applicationService,
    IOptions<ExchangeOptions>? options = null,
    ITicketIssuanceService? ticketIssuanceService = null) : IExchangeReviewService
{
    private readonly ExchangeOptions exchangeOptions = options?.Value ?? new ExchangeOptions();
    public async Task<OrderTicketResult<PagedExchangeResponse>> ListAsync(
        AdminExchangeListQuery query,
        CancellationToken cancellationToken = default)
    {
        var offset = ((long)query.Page - 1) * query.PageSize;
        if (query.Page < 1 || query.PageSize is < 1 or > 100 || offset > int.MaxValue)
            return Invalid<PagedExchangeResponse>("EXCHANGE_INVALID_PAGING",
                "Page must be positive and pageSize must be between 1 and 100.");

        var requests = dbContext.Set<ExchangeRequest>().AsNoTracking().AsQueryable();
        if (query.ApproveStatus.HasValue)
        {
            var value = query.ApproveStatus.Value.ToString();
            requests = requests.Where(item => item.ApproveStatus == value);
        }
        if (query.ExchangeStatus.HasValue)
        {
            var value = query.ExchangeStatus.Value.ToString();
            requests = requests.Where(item => item.ExchangeStatus == value);
        }
        if (query.OriginalOrderId.HasValue)
            requests = requests.Where(item => item.OrderId == query.OriginalOrderId.Value);
        if (query.UserId.HasValue)
            requests = requests.Where(item => item.UserId == query.UserId.Value);
        var exchangeNo = query.ExchangeNo?.Trim();
        if (!string.IsNullOrEmpty(exchangeNo))
            requests = requests.Where(item => item.ExchangeNo == exchangeNo);

        var total = await requests.CountAsync(cancellationToken);
        var rows = await requests.OrderByDescending(item => item.CreateTime)
            .ThenByDescending(item => item.ExchangeId)
            .Skip((int)offset).Take(query.PageSize)
            .Select(item => new { item.ExchangeId, item.UserId })
            .ToListAsync(cancellationToken);
        var summaries = new List<ExchangeSummaryResponse>(rows.Count);
        foreach (var row in rows)
        {
            var detail = await applicationService.GetAsync(row.UserId, row.ExchangeId, cancellationToken);
            if (!detail.IsSuccess)
                return CopyFailure<ExchangeResponse, PagedExchangeResponse>(detail);
            var value = detail.Value!;
            summaries.Add(new ExchangeSummaryResponse(
                value.ExchangeId, value.ExchangeNo, value.OriginalOrderId, value.ChildOrderId,
                value.AmountDue, value.ApproveStatus, value.ExchangeStatus,
                value.ExpireTime, value.CreateTime, value.CompleteTime));
        }
        return OrderTicketResult<PagedExchangeResponse>.Success(
            new PagedExchangeResponse(summaries, query.Page, query.PageSize, total));
    }

    public async Task<OrderTicketResult<ExchangeResponse>> GetAsync(
        long exchangeId,
        CancellationToken cancellationToken = default)
    {
        var userId = await dbContext.Set<ExchangeRequest>().AsNoTracking()
            .Where(item => item.ExchangeId == exchangeId)
            .Select(item => (long?)item.UserId).SingleOrDefaultAsync(cancellationToken);
        return userId.HasValue
            ? await applicationService.GetAsync(userId.Value, exchangeId, cancellationToken)
            : NotFound<ExchangeResponse>("EXCHANGE_NOT_FOUND", "The exchange request does not exist.");
    }

    public async Task<OrderTicketResult<ExchangeResponse>> RejectAsync(
        string actor,
        long exchangeId,
        RejectExchangeRequest request,
        CancellationToken cancellationToken = default)
    {
        var remark = request?.Remark?.Trim();
        if (string.IsNullOrEmpty(remark) || remark.Length > 500)
            return Invalid<ExchangeResponse>("EXCHANGE_REVIEW_REMARK_INVALID",
                "Reject remark is required and must not exceed 500 characters.");
        return await RestoreAsync(exchangeId, actor, remark, false, cancellationToken);
    }

    public async Task<OrderTicketResult<ExchangeResponse>> ApproveAsync(
        string actor,
        long exchangeId,
        ApproveExchangeRequest request,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var result = await ApproveOnceAsync(actor, exchangeId, request, cancellationToken);
            if (attempt == 0 && result.ErrorCode == "EXCHANGE_TICKET_IDENTIFIER_COLLISION")
                continue;
            return result.ErrorCode == "EXCHANGE_TICKET_IDENTIFIER_COLLISION"
                ? Conflict<ExchangeResponse>("EXCHANGE_REVIEW_CONFLICT",
                    "Generated ticket identifiers collided twice.")
                : result;
        }
        return Conflict<ExchangeResponse>("EXCHANGE_REVIEW_CONFLICT",
            "The exchange request could not be approved.");
    }

    private async Task<OrderTicketResult<ExchangeResponse>> ApproveOnceAsync(
        string actor,
        long exchangeId,
        ApproveExchangeRequest request,
        CancellationToken cancellationToken = default)
    {
        var remark = request?.Remark?.Trim();
        if (remark?.Length > 500)
            return Invalid<ExchangeResponse>("EXCHANGE_REVIEW_REMARK_INVALID",
                "Approve remark must not exceed 500 characters.");
        if (string.IsNullOrEmpty(remark)) remark = null;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await lockCoordinator.LockExchangeRequestAsync(exchangeId, cancellationToken))
        {
            await RollbackAndClearAsync(transaction, cancellationToken);
            return NotFound<ExchangeResponse>("EXCHANGE_NOT_FOUND", "The exchange request does not exist.");
        }
        dbContext.ChangeTracker.Clear();
        var exchange = await dbContext.Set<ExchangeRequest>()
            .Include(item => item.Items).ThenInclude(item => item.OrderItem)!.ThenInclude(item => item!.ETicket)
            .Include(item => item.Items).ThenInclude(item => item.NewOrderItem)
            .SingleOrDefaultAsync(item => item.ExchangeId == exchangeId, cancellationToken);
        if (exchange is null || exchange.Items.Count == 0 ||
            exchange.Items.Any(item => item.OrderItem?.ETicket is null || item.NewOrderItem is null))
        {
            await RollbackAndClearAsync(transaction, cancellationToken);
            return Conflict<ExchangeResponse>("EXCHANGE_DATA_INCONSISTENT", "The exchange aggregate is incomplete.");
        }
        if (exchange.ApproveStatus != "PENDING" || exchange.ExchangeStatus != "PENDING")
        {
            await RollbackAndClearAsync(transaction, cancellationToken);
                return Conflict<ExchangeResponse>("EXCHANGE_ALREADY_REVIEWED", "The exchange request has already been reviewed.");
        }
        if (!await lockCoordinator.LockOrderAsync(exchange.OrderId, cancellationToken))
        {
            await RollbackAndClearAsync(transaction, cancellationToken);
            return Conflict<ExchangeResponse>("EXCHANGE_DATA_INCONSISTENT", "The original order is inconsistent.");
        }
        var childIds = exchange.Items.Select(item => item.NewOrderItem!.OrderId).Distinct().ToArray();
        if (childIds.Length != 1 || !await lockCoordinator.LockOrderAsync(childIds[0], cancellationToken))
        {
            await RollbackAndClearAsync(transaction, cancellationToken);
            return Conflict<ExchangeResponse>("EXCHANGE_DATA_INCONSISTENT", "The exchange child order is inconsistent.");
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
            return Conflict<ExchangeResponse>(
                "EXCHANGE_RESOURCE_CONFLICT",
                "An exchange resource changed while the request was being locked.");
        }
        dbContext.ChangeTracker.Clear();
        exchange = await dbContext.Set<ExchangeRequest>()
            .Include(item => item.Items).ThenInclude(item => item.OrderItem)!
                .ThenInclude(item => item!.ETicket)
            .Include(item => item.Items).ThenInclude(item => item.NewOrderItem)
            .SingleAsync(item => item.ExchangeId == exchangeId, cancellationToken);
        var child = await dbContext.Set<Order>()
            .Include(item => item.Items).ThenInclude(item => item.ETicket)
            .Include(item => item.Payments)
            .SingleAsync(item => item.OrderId == childIds[0], cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (child.ExpireTime <= now)
        {
            await RollbackAndClearAsync(transaction, cancellationToken);
            await transaction.DisposeAsync();
            var expiration = await ExpireAsync(
                exchangeId, "exchange-expiration", cancellationToken);
            if (!expiration.IsSuccess)
                return expiration;
            return Conflict<ExchangeResponse>("EXCHANGE_REVIEW_EXPIRED", "The exchange review period has expired.");
        }
        var newIds = exchange.Items.Select(item => item.NewOrderItemId).ToArray();
        var targetReservations = await dbContext.Set<SeatReservation>()
            .Where(item => item.OrderItemId.HasValue && newIds.Contains(item.OrderItemId.Value))
            .ToListAsync(cancellationToken);
        if (child.OrderType != "EXCHANGE" || child.OrderStatus != "PENDING_PAY" ||
            child.TotalAmount != exchange.PriceDiff + exchange.ExchangeFee ||
            exchange.Items.Any(item => item.OrderItem!.ItemStatus != "EXCHANGING" ||
                                       item.OrderItem.ETicket!.TicketStatus != "EXCHANGING") ||
            targetReservations.Count != newIds.Length ||
            targetReservations.Any(item => item.ReservationStatus != "ACTIVE"))
        {
            await RollbackAndClearAsync(transaction, cancellationToken);
            return Conflict<ExchangeResponse>("EXCHANGE_REVIEW_CONFLICT", "Exchange resources changed before approval.");
        }

        exchange.ApproveStatus = "APPROVED";
        exchange.ReviewBy = actor;
        exchange.ReviewTime = now;
        exchange.ReviewRemark = remark;
        exchange.UpdateBy = actor;
        if (child.TotalAmount > 0m)
        {
            exchange.ExchangeStatus = "PROCESSING";
            child.ExpireTime = now.AddMinutes(exchangeOptions.PaymentExpireMinutes);
            child.UpdateBy = actor;
        }
        else
        {
            var issuance = ticketIssuanceService ?? throw new InvalidOperationException(
                "Ticket issuance service is required to approve a zero-amount exchange.");
            var payment = CreatePayment(child, exchange.UserId, actor, now, "BALANCE", "SUCCESS", 0m);
            child.Payments.Add(payment);
            child.PayTime = now;
            var issuanceResult = issuance.Issue(
                child, TicketIssuanceContext.Exchange, actor,
                new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc)));
            if (!issuanceResult.IsSuccess)
            {
                await RollbackAndClearAsync(transaction, cancellationToken);
                return OrderTicketResult<ExchangeResponse>.Fail(
                    issuanceResult.Failure, issuanceResult.ErrorCode!, issuanceResult.Message!);
            }
            var originalIds = exchange.Items.Select(item => item.OrderItemId).ToArray();
            var originalReservations = await dbContext.Set<SeatReservation>()
                .Where(item => item.OrderItemId.HasValue && originalIds.Contains(item.OrderItemId.Value))
                .ToListAsync(cancellationToken);
            if (originalReservations.Count != originalIds.Length ||
                originalReservations.Any(item => item.ReservationStatus != "ACTIVE"))
            {
                await RollbackAndClearAsync(transaction, cancellationToken);
                return Conflict<ExchangeResponse>("EXCHANGE_RESERVATION_DATA_INCONSISTENT",
                    "Original seat reservations are inconsistent.");
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
                return Conflict<ExchangeResponse>("EXCHANGE_TICKET_IDENTIFIER_COLLISION",
                    "A generated ticket identifier collided and will be retried.");
            }
            return Conflict<ExchangeResponse>("EXCHANGE_REVIEW_CONFLICT",
                "The exchange request conflicted with another operation.");
        }
        dbContext.ChangeTracker.Clear();
        return await applicationService.GetAsync(exchange.UserId, exchangeId, cancellationToken);
    }

    public Task<OrderTicketResult<ExchangeResponse>> ExpireAsync(
        long exchangeId,
        string actor,
        CancellationToken cancellationToken = default) =>
        RestoreAsync(exchangeId, actor, "Exchange request expired.", true, cancellationToken);

    private async Task<OrderTicketResult<ExchangeResponse>> RestoreAsync(
        long exchangeId,
        string actor,
        string remark,
        bool requireExpired,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await lockCoordinator.LockExchangeRequestAsync(exchangeId, cancellationToken))
        {
            await RollbackAndClearAsync(transaction, cancellationToken);
            return NotFound<ExchangeResponse>("EXCHANGE_NOT_FOUND", "The exchange request does not exist.");
        }

        dbContext.ChangeTracker.Clear();
        var exchange = await dbContext.Set<ExchangeRequest>()
            .Include(item => item.Items).ThenInclude(item => item.OrderItem)!.ThenInclude(item => item!.ETicket)
            .Include(item => item.Items).ThenInclude(item => item.NewOrderItem)
            .SingleOrDefaultAsync(item => item.ExchangeId == exchangeId, cancellationToken);
        if (exchange is null || exchange.Items.Count == 0 ||
            exchange.Items.Any(item => item.OrderItem?.ETicket is null || item.NewOrderItem is null))
        {
            await RollbackAndClearAsync(transaction, cancellationToken);
            return Conflict<ExchangeResponse>("EXCHANGE_DATA_INCONSISTENT",
                "The exchange aggregate is incomplete.");
        }

        var childIds = exchange.Items.Select(item => item.NewOrderItem!.OrderId).Distinct().ToArray();
        if (!await lockCoordinator.LockOrderAsync(exchange.OrderId, cancellationToken) ||
            childIds.Length != 1 || !await lockCoordinator.LockOrderAsync(childIds[0], cancellationToken))
        {
            await RollbackAndClearAsync(transaction, cancellationToken);
            return Conflict<ExchangeResponse>("EXCHANGE_DATA_INCONSISTENT",
                "The exchange child order is inconsistent.");
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
            return Conflict<ExchangeResponse>(
                "EXCHANGE_RESOURCE_CONFLICT",
                "An exchange resource changed while the request was being locked.");
        }
        dbContext.ChangeTracker.Clear();
        exchange = await dbContext.Set<ExchangeRequest>()
            .Include(item => item.Items).ThenInclude(item => item.OrderItem)!
                .ThenInclude(item => item!.ETicket)
            .Include(item => item.Items).ThenInclude(item => item.NewOrderItem)
            .SingleAsync(item => item.ExchangeId == exchangeId, cancellationToken);
        var child = await dbContext.Set<Order>().SingleAsync(item => item.OrderId == childIds[0], cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var isPending = exchange.ApproveStatus == "PENDING" && exchange.ExchangeStatus == "PENDING";
        var isProcessing = exchange.ApproveStatus == "APPROVED" && exchange.ExchangeStatus == "PROCESSING";
        if (!isPending && !isProcessing)
        {
            await RollbackAndClearAsync(transaction, cancellationToken);
            await transaction.DisposeAsync();
            return await applicationService.GetAsync(exchange.UserId, exchangeId, cancellationToken);
        }
        if (!requireExpired && !isPending)
        {
            await RollbackAndClearAsync(transaction, cancellationToken);
            return Conflict<ExchangeResponse>("EXCHANGE_ALREADY_REVIEWED",
                "The exchange request has already been reviewed.");
        }
        if (requireExpired && child.ExpireTime > now)
        {
            await RollbackAndClearAsync(transaction, cancellationToken);
            return Conflict<ExchangeResponse>("EXCHANGE_NOT_EXPIRED", "The exchange request has not expired.");
        }

        var newItemIds = exchange.Items.Select(item => item.NewOrderItemId).ToArray();
        var reservations = await dbContext.Set<SeatReservation>()
            .Where(item => item.OrderItemId.HasValue && newItemIds.Contains(item.OrderItemId.Value))
            .ToListAsync(cancellationToken);
        if (child.OrderType != "EXCHANGE" || child.OrderStatus != "PENDING_PAY" ||
            exchange.Items.Any(item => item.OrderItem!.ItemStatus != "EXCHANGING" ||
                                       item.OrderItem.ETicket!.TicketStatus != "EXCHANGING") ||
            reservations.Count != newItemIds.Length ||
            reservations.Any(item => item.ReservationStatus != "ACTIVE"))
        {
            await RollbackAndClearAsync(transaction, cancellationToken);
            return Conflict<ExchangeResponse>("EXCHANGE_RESTORE_CONFLICT",
                "Exchange resources are not in a restorable state.");
        }

        foreach (var item in exchange.Items)
        {
            item.OrderItem!.ItemStatus = "NORMAL";
            item.OrderItem.UpdateBy = actor;
            item.OrderItem.ETicket!.TicketStatus = "UNUSED";
            item.OrderItem.ETicket.UpdateBy = actor;
        }
        foreach (var reservation in reservations)
        {
            reservation.ReservationStatus = "CANCELLED";
            reservation.CancelTime ??= now;
            reservation.UpdateBy = actor;
        }
        child.OrderStatus = "CANCELLED";
        child.CancelTime ??= now;
        child.UpdateBy = actor;
        if (isPending)
        {
            exchange.ApproveStatus = "REJECTED";
            exchange.ReviewBy = actor;
            exchange.ReviewTime = now;
            exchange.ReviewRemark = remark;
        }
        exchange.ExchangeStatus = "FAILED";
        exchange.CompleteTime ??= now;
        exchange.UpdateBy = actor;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await RollbackAndClearAsync(transaction, CancellationToken.None);
            return Conflict<ExchangeResponse>("EXCHANGE_REVIEW_CONFLICT",
                "The exchange request conflicted with another operation.");
        }

        dbContext.ChangeTracker.Clear();
        return await applicationService.GetAsync(exchange.UserId, exchangeId, cancellationToken);
    }

    private async Task RollbackAndClearAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
    }

    private static OrderTicketResult<TTarget> CopyFailure<TSource, TTarget>(OrderTicketResult<TSource> source) =>
        OrderTicketResult<TTarget>.Fail(source.Failure, source.ErrorCode!, source.Message!);
    private static OrderTicketResult<T> Invalid<T>(string code, string message) =>
        OrderTicketResult<T>.Fail(OrderTicketFailure.InvalidRequest, code, message);
    private static OrderTicketResult<T> NotFound<T>(string code, string message) =>
        OrderTicketResult<T>.Fail(OrderTicketFailure.NotFound, code, message);
    private static OrderTicketResult<T> Conflict<T>(string code, string message) =>
        OrderTicketResult<T>.Fail(OrderTicketFailure.Conflict, code, message);

    internal static Payment CreatePayment(
        Order child, long userId, string actor, DateTime now,
        string channel, string status, decimal amount) => new()
    {
        PaymentNo = CreateBusinessNumber("EXP", now),
        OrderId = child.OrderId,
        UserId = userId,
        PayAmount = amount,
        PayChannel = channel,
        PayStatus = status,
        TradeNo = status == "SUCCESS" ? CreateBusinessNumber("MOCK", now) : null,
        CallbackData = $"{{\"exchangeResult\":\"{status}\"}}",
        CallbackTime = now,
        PayTime = status == "SUCCESS" ? now : null,
        RefundAmount = 0m,
        CreateBy = actor,
        UpdateBy = actor,
    };

    private static string CreateBusinessNumber(string prefix, DateTime now) =>
        $"{prefix}{now:yyyyMMddHHmmssfff}{Guid.NewGuid():N}"[..28].ToUpperInvariant();
}
