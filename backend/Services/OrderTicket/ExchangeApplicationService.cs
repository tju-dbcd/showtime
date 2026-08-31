using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ShowtimeBackend.Common;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Entities.SeatZone;
using ShowtimeBackend.Entities.ShowSession;
using ShowtimeBackend.Services.ShowSession;
using ShowtimeBackend.Services.SeatZone;

namespace ShowtimeBackend.Services.OrderTicket;

public sealed class ExchangeApplicationService(
    AppDbContext dbContext,
    ExchangePolicyEngine policyEngine,
    TimeProvider timeProvider,
    IExchangeLockCoordinator? lockCoordinator = null,
    IOptions<ExchangeOptions>? options = null,
    ISeatLockGuard? seatLockGuard = null) : IExchangeApplicationService
{
    private const decimal MaxOracleAmount = 99_999_999.99m;
    private readonly ExchangeOptions exchangeOptions = options?.Value ?? new ExchangeOptions();

    public async Task<OrderTicketResult<ExchangeQuoteResponse>> QuoteAsync(
        long userId,
        long orderId,
        ExchangeQuoteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || request.TargetSessionId <= 0 ||
            request.TargetItems is null || request.TargetItems.Count == 0 ||
            request.TargetItems.Any(item =>
                item.OriginalOrderItemId <= 0 || item.SeatId <= 0 ||
                item.PriceStrategyId <= 0 || string.IsNullOrWhiteSpace(item.LockToken) ||
                item.LockToken.Length > 64) ||
            HasDuplicates(request.TargetItems.Select(item => item.OriginalOrderItemId)))
        {
            return Invalid("EXCHANGE_REQUEST_INVALID", "Exchange target items must be valid and unique.");
        }

        if (HasDuplicates(request.TargetItems.Select(item => item.SeatId)) ||
            request.TargetItems.Select(item => item.LockToken)
                .Distinct(StringComparer.Ordinal).Count() != request.TargetItems.Count)
        {
            return Invalid("EXCHANGE_TARGET_DUPLICATED", "Exchange target seats and lock tokens must be unique.");
        }

        var order = await dbContext.Set<Order>()
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.OrderId == orderId && item.UserId == userId,
                cancellationToken);
        if (order is null)
        {
            return NotFound("EXCHANGE_NOT_FOUND", "The order does not exist.");
        }

        if (order.OrderType == "EXCHANGE" ||
            order.OrderStatus is not ("ISSUED" or "PART_REFUND"))
        {
            return Conflict("EXCHANGE_ORDER_NOT_ELIGIBLE", "The order cannot be exchanged.");
        }

        var originalSession = await dbContext.Set<ShowtimeBackend.Entities.ShowSession.ShowSession>()
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.SessionId == order.SessionId, cancellationToken);
        if (originalSession is null)
        {
            return Conflict("EXCHANGE_ORDER_NOT_ELIGIBLE", "The original session is unavailable.");
        }

        var targetSession = await dbContext.Set<ShowtimeBackend.Entities.ShowSession.ShowSession>()
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.SessionId == request.TargetSessionId, cancellationToken);
        if (targetSession is null)
        {
            return NotFound("EXCHANGE_TARGET_SESSION_NOT_FOUND", "The target session does not exist.");
        }

        if (targetSession.SessionStatus is not ("PRESALE" or "ONSALE"))
        {
            return Conflict("EXCHANGE_TARGET_SESSION_NOT_ELIGIBLE", "The target session is not on sale.");
        }

        if (targetSession.ShowId != originalSession.ShowId)
        {
            return Conflict("EXCHANGE_CROSS_SHOW_NOT_ALLOWED", "Exchanging across shows is not allowed.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var policyRules = await dbContext.Set<ExchangePolicy>()
            .AsNoTracking()
            .Where(item => item.Status == 1 &&
                (item.ShowId == originalSession.ShowId || item.ShowId == null))
            .Select(item => new ExchangePolicyRule(
                item.PolicyId, item.ShowId, item.PolicyName,
                item.ExchangeDeadlineHour, item.ExchangeFee,
                item.AllowCrossSession, item.Priority, item.Status))
            .ToListAsync(cancellationToken);
        var policy = policyEngine.Select(new ExchangePolicyInput(
            now, originalSession.StartTime, originalSession.ShowId,
            originalSession.SessionId != targetSession.SessionId, policyRules));
        if (policy is null)
        {
            return Conflict(
                originalSession.SessionId == targetSession.SessionId
                    ? "EXCHANGE_POLICY_NOT_APPLICABLE"
                    : "EXCHANGE_CROSS_SESSION_NOT_ALLOWED",
                "No exchange policy applies to this request.");
        }

        var originalItemIds = request.TargetItems
            .Select(item => item.OriginalOrderItemId).ToArray();
        var originalItems = await dbContext.Set<OrderItem>()
            .AsNoTracking()
            .Include(item => item.ETicket)
            .Where(item => originalItemIds.Contains(item.OrderItemId))
            .ToDictionaryAsync(item => item.OrderItemId, cancellationToken);
        if (originalItems.Count != originalItemIds.Length ||
            originalItems.Values.Any(item => item.OrderId != orderId))
        {
            return Conflict("EXCHANGE_ITEM_NOT_ELIGIBLE", "An original order item cannot be exchanged.");
        }

        if (await HasActiveExchangeAsync(originalItemIds, cancellationToken) ||
            await HasActiveRefundAsync(originalItemIds, cancellationToken))
        {
            return Conflict("EXCHANGE_ACTIVE_REQUEST_EXISTS", "A ticket has an active refund or exchange request.");
        }

        if (await HasCompletedExchangeHistoryAsync(originalItemIds, cancellationToken))
        {
            return Conflict("EXCHANGE_TICKET_HISTORY_CONFLICT", "A ticket has conflicting exchange history.");
        }

        if (originalItems.Values.Any(item => item.ItemStatus != "NORMAL"))
        {
            return Conflict("EXCHANGE_ITEM_NOT_ELIGIBLE", "An original order item cannot be exchanged.");
        }

        if (originalItems.Values.Any(item => item.ETicket?.TicketStatus != "UNUSED"))
        {
            return Conflict("EXCHANGE_TICKET_NOT_UNUSED", "An original ticket is not unused.");
        }

        var targetSeatIds = request.TargetItems.Select(item => item.SeatId).ToArray();
        var seats = await dbContext.Set<Seat>().AsNoTracking()
            .Where(item => targetSeatIds.Contains(item.SeatId))
            .ToDictionaryAsync(item => item.SeatId, cancellationToken);
        var strategyIds = request.TargetItems.Select(item => item.PriceStrategyId).Distinct().ToArray();
        var strategies = await dbContext.Set<PriceStrategy>().AsNoTracking()
            .Where(item => strategyIds.Contains(item.PriceStrategyId))
            .ToDictionaryAsync(item => item.PriceStrategyId, cancellationToken);
        var locks = await dbContext.Set<SeatLock>().AsNoTracking()
            .Where(item => item.SessionId == targetSession.SessionId && item.UserId == userId &&
                           targetSeatIds.Contains(item.SeatId) && item.LockStatus == "ACTIVE" &&
                           item.ExpireTime > now)
            .ToDictionaryAsync(item => item.SeatId, cancellationToken);
        var dynamicRules = await dbContext.Set<DynamicPricingRule>().AsNoTracking()
            .Where(item => item.SessionId == targetSession.SessionId && item.Status == "ENABLED")
            .ToListAsync(cancellationToken);

        var quoteItems = new List<ExchangeQuoteItemResponse>(request.TargetItems.Count);
        foreach (var requestedItem in request.TargetItems)
        {
            if (!seats.TryGetValue(requestedItem.SeatId, out var seat) ||
                !seat.IsSellable || seat.SeatStatus != "ENABLED")
            {
                return Conflict("EXCHANGE_TARGET_SEAT_UNAVAILABLE", "A target seat is unavailable.");
            }

            if (!strategies.TryGetValue(requestedItem.PriceStrategyId, out var strategy) ||
                strategy.SessionId != targetSession.SessionId ||
                strategy.SeatSectionId != seat.SeatSectionId || strategy.Status != "ENABLED")
            {
                return Invalid("EXCHANGE_TARGET_PRICE_INVALID", "A target price strategy is invalid.");
            }

            if (!locks.TryGetValue(requestedItem.SeatId, out var seatLock) ||
                !string.Equals(seatLock.LockToken, requestedItem.LockToken, StringComparison.Ordinal))
            {
                return Conflict("EXCHANGE_SEAT_LOCK_INVALID", "A target seat lock is invalid or expired.");
            }

            var originalItem = originalItems[requestedItem.OriginalOrderItemId];
            var newUnitPrice = PricingChange.CalculateRealtimePrice(
                strategy.Price, targetSession.StartTime, seatLock.CreateTime,
                strategy.SeatSectionId, dynamicRules);
            if (!IsOracleAmount(originalItem.UnitPrice) || !IsOracleAmount(newUnitPrice))
            {
                return Invalid("EXCHANGE_AMOUNT_INVALID", "An exchange amount is outside the supported range.");
            }

            quoteItems.Add(new ExchangeQuoteItemResponse(
                originalItem.OrderItemId, requestedItem.SeatId, requestedItem.PriceStrategyId,
                originalItem.RealNameId, originalItem.UnitPrice, newUnitPrice));
        }

        var originalDeduction = quoteItems.Sum(item => item.OriginalUnitPrice);
        var targetAmount = quoteItems.Sum(item => item.NewUnitPrice);
        if (!IsOracleAmount(originalDeduction) || !IsOracleAmount(targetAmount) ||
            !IsOracleAmount(policy.ExchangeFee))
        {
            return Invalid("EXCHANGE_AMOUNT_INVALID", "An exchange amount is outside the supported range.");
        }

        if (targetAmount < originalDeduction)
        {
            return Conflict("EXCHANGE_PRICE_DOWN_NOT_SUPPORTED", "The target tickets cannot cost less than the originals.");
        }

        var priceDiff = targetAmount - originalDeduction;
        var amountDue = priceDiff + policy.ExchangeFee;
        if (!IsOracleAmount(priceDiff) || !IsOracleAmount(amountDue))
        {
            return Invalid("EXCHANGE_AMOUNT_INVALID", "The amount due is outside the supported range.");
        }

        return OrderTicketResult<ExchangeQuoteResponse>.Success(new ExchangeQuoteResponse(
            now, order.OrderId, originalSession.SessionId, targetSession.SessionId,
            originalDeduction, targetAmount, priceDiff, policy.ExchangeFee, amountDue,
            policy.PolicyId, policy.PolicyName, quoteItems));
    }

    public async Task<OrderTicketResult<ExchangeResponse>> CreateAsync(
        long userId,
        string actor,
        long orderId,
        CreateExchangeRequest request,
        CancellationToken cancellationToken = default)
    {
        var reason = request?.Reason?.Trim();
        if (request is null || reason?.Length > 500)
        {
            return Invalid<ExchangeResponse>(
                "EXCHANGE_REQUEST_INVALID", "The exchange request is invalid.");
        }

        if (string.IsNullOrEmpty(reason))
        {
            reason = null;
        }

        var coordinator = lockCoordinator ?? new OracleExchangeLockCoordinator(dbContext);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (!await coordinator.LockOrderAsync(orderId, cancellationToken))
            {
                await RollbackAndClearAsync(transaction, cancellationToken);
                return NotFound<ExchangeResponse>("EXCHANGE_NOT_FOUND", "The order does not exist.");
            }

            dbContext.ChangeTracker.Clear();
            var quoteResult = await QuoteAsync(
                userId,
                orderId,
                new ExchangeQuoteRequest(request.TargetSessionId, request.TargetItems),
                cancellationToken);
            if (!quoteResult.IsSuccess)
            {
                await RollbackAndClearAsync(transaction, cancellationToken);
                return CopyFailure<ExchangeQuoteResponse, ExchangeResponse>(quoteResult);
            }

            var quote = quoteResult.Value!;
            var originalIds = quote.Items
                .Select(item => item.OriginalOrderItemId)
                .ToArray();
            if (!await ExchangeLockProtocol.LockItemsTicketsAndReservationsAsync(
                    dbContext,
                    coordinator,
                    originalIds,
                    cancellationToken))
            {
                await RollbackAndClearAsync(transaction, cancellationToken);
                return Conflict<ExchangeResponse>(
                    "EXCHANGE_RESOURCE_CONFLICT",
                    "An original exchange resource changed before creation.");
            }

            var targetSeatIds = request.TargetItems.Select(item => item.SeatId).ToArray();
            var targetLocks = await dbContext.Set<SeatLock>()
                .Where(item => item.SessionId == request.TargetSessionId &&
                               item.UserId == userId && targetSeatIds.Contains(item.SeatId))
                .OrderBy(item => item.SeatLockId)
                .ToListAsync(cancellationToken);
            if (targetLocks.Count != targetSeatIds.Length)
            {
                await RollbackAndClearAsync(transaction, cancellationToken);
                return Conflict<ExchangeResponse>(
                    "EXCHANGE_SEAT_LOCK_INVALID", "A target seat lock is invalid or expired.");
            }

            foreach (var targetLock in targetLocks)
            {
                if (!await coordinator.LockSeatLockAsync(targetLock.SeatLockId, cancellationToken))
                {
                    await RollbackAndClearAsync(transaction, cancellationToken);
                    return Conflict<ExchangeResponse>(
                        "EXCHANGE_SEAT_LOCK_INVALID", "A target seat lock changed.");
                }
            }

            dbContext.ChangeTracker.Clear();
            quoteResult = await QuoteAsync(
                userId,
                orderId,
                new ExchangeQuoteRequest(request.TargetSessionId, request.TargetItems),
                cancellationToken);
            if (!quoteResult.IsSuccess)
            {
                await RollbackAndClearAsync(transaction, cancellationToken);
                return CopyFailure<ExchangeQuoteResponse, ExchangeResponse>(quoteResult);
            }

            quote = quoteResult.Value!;
            var now = quote.QuotedAt;
            var originalItems = await dbContext.Set<OrderItem>()
                .Include(item => item.ETicket)
                .Where(item => originalIds.Contains(item.OrderItemId))
                .OrderBy(item => item.OrderItemId)
                .ToListAsync(cancellationToken);
            targetLocks = await dbContext.Set<SeatLock>()
                .Where(item => item.SessionId == request.TargetSessionId &&
                               item.UserId == userId && targetSeatIds.Contains(item.SeatId))
                .ToListAsync(cancellationToken);
            if (originalItems.Count != originalIds.Length || targetLocks.Count != targetSeatIds.Length ||
                originalItems.Any(item => item.OrderId != orderId || item.ItemStatus != "NORMAL" ||
                                          item.ETicket?.TicketStatus != "UNUSED") ||
                targetLocks.Any(item => item.LockStatus != "ACTIVE" || item.ExpireTime <= now))
            {
                await RollbackAndClearAsync(transaction, cancellationToken);
                return Conflict<ExchangeResponse>(
                    "EXCHANGE_CREATE_CONFLICT", "Exchange resources changed before creation.");
            }

            var childOrder = new Order
            {
                OrderNo = CreateBusinessNumber("EXO", now),
                UserId = userId,
                SessionId = request.TargetSessionId,
                OrderType = "EXCHANGE",
                ParentOrderId = orderId,
                TotalAmount = quote.AmountDue,
                DiscountAmount = 0m,
                TicketCount = quote.Items.Count,
                OrderStatus = "PENDING_PAY",
                ExpireTime = now.AddMinutes(exchangeOptions.ReviewExpireMinutes),
                Source = "WEB",
                Remark = reason,
                CreateBy = actor,
                UpdateBy = actor,
            };
            foreach (var quoteItem in quote.Items)
            {
                childOrder.Items.Add(new OrderItem
                {
                    SeatId = quoteItem.TargetSeatId,
                    PriceStrategyId = quoteItem.TargetPriceStrategyId,
                    RealNameId = quoteItem.RealNameId,
                    UnitPrice = quoteItem.NewUnitPrice,
                    ItemStatus = "NORMAL",
                    CreateBy = actor,
                    UpdateBy = actor,
                });
            }

            var exchange = new ExchangeRequest
            {
                ExchangeNo = CreateBusinessNumber("EXC", now),
                OrderId = orderId,
                UserId = userId,
                OrigSessionId = quote.OrigSessionId,
                TargetSessionId = quote.TargetSessionId,
                ExchangeReason = reason,
                ExchangeFee = quote.ExchangeFee,
                PriceDiff = quote.PriceDiff,
                AppliedPolicyId = quote.AppliedPolicyId,
                ApproveStatus = "PENDING",
                ExchangeStatus = "PENDING",
                CreateBy = actor,
                UpdateBy = actor,
            };
            dbContext.Add(childOrder);
            dbContext.Add(exchange);
            await dbContext.SaveChangesAsync(cancellationToken);

            foreach (var quoteItem in quote.Items)
            {
                var newItem = childOrder.Items.Single(item => item.SeatId == quoteItem.TargetSeatId);
                var originalItem = originalItems.Single(item =>
                    item.OrderItemId == quoteItem.OriginalOrderItemId);
                var targetLock = targetLocks.Single(item => item.SeatId == quoteItem.TargetSeatId);

                dbContext.Add(new ExchangeItem
                {
                    ExchangeId = exchange.ExchangeId,
                    OrderItemId = originalItem.OrderItemId,
                    NewOrderItemId = newItem.OrderItemId,
                    CreateBy = actor,
                    UpdateBy = actor,
                });
                dbContext.Add(new SeatReservation
                {
                    SessionId = request.TargetSessionId,
                    SeatId = quoteItem.TargetSeatId,
                    OrderItemId = newItem.OrderItemId,
                    SeatLockId = targetLock.SeatLockId,
                    ReservationType = "ORDER",
                    ReservationStatus = "ACTIVE",
                    ReserveTime = now,
                    HoldReason = "EXCHANGE",
                    CreateBy = actor,
                    UpdateBy = actor,
                });
                targetLock.LockStatus = "CONVERTED";
                targetLock.UpdateBy = actor;
                originalItem.ItemStatus = "EXCHANGING";
                originalItem.UpdateBy = actor;
                originalItem.ETicket!.TicketStatus = "EXCHANGING";
                originalItem.ETicket.UpdateBy = actor;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            if (seatLockGuard is not null)
            {
                foreach (var item in request.TargetItems)
                {
                    try
                    {
                        await seatLockGuard.ReleaseAsync(request.TargetSessionId, item.SeatId, item.LockToken);
                    }
                    catch
                    {
                        // Redis is an acceleration layer; the committed DB state and TTL are authoritative.
                    }
                }
            }

            dbContext.ChangeTracker.Clear();
            return await LoadResponseAsync(exchange.ExchangeId, userId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await RollbackAndClearAsync(transaction, CancellationToken.None);
            throw;
        }
        catch (DbUpdateException)
        {
            await RollbackAndClearAsync(transaction, CancellationToken.None);
            return Conflict<ExchangeResponse>(
                "EXCHANGE_CREATE_CONFLICT", "Exchange creation conflicted with another operation.");
        }
    }

    public async Task<OrderTicketResult<PagedExchangeResponse>> ListAsync(
        long userId,
        long orderId,
        ExchangeListQuery query,
        CancellationToken cancellationToken = default)
    {
        var offset = ((long)query.Page - 1) * query.PageSize;
        if (query.Page < 1 || query.PageSize is < 1 or > 100 || offset > int.MaxValue)
        {
            return Invalid<PagedExchangeResponse>(
                "EXCHANGE_INVALID_PAGING", "Page must be positive and pageSize must be between 1 and 100.");
        }

        var ownsOrder = await dbContext.Set<Order>().AsNoTracking()
            .AnyAsync(item => item.OrderId == orderId && item.UserId == userId, cancellationToken);
        if (!ownsOrder)
        {
            return NotFound<PagedExchangeResponse>("EXCHANGE_NOT_FOUND", "The order does not exist.");
        }

        var requests = dbContext.Set<ExchangeRequest>().AsNoTracking()
            .Where(item => item.OrderId == orderId && item.UserId == userId);
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

        var total = await requests.CountAsync(cancellationToken);
        var ids = await requests.OrderByDescending(item => item.CreateTime)
            .ThenByDescending(item => item.ExchangeId)
            .Skip((int)offset).Take(query.PageSize)
            .Select(item => item.ExchangeId).ToListAsync(cancellationToken);
        var summaries = new List<ExchangeSummaryResponse>(ids.Count);
        foreach (var id in ids)
        {
            var detail = await LoadResponseAsync(id, userId, cancellationToken);
            if (!detail.IsSuccess)
            {
                return CopyFailure<ExchangeResponse, PagedExchangeResponse>(detail);
            }
            var value = detail.Value!;
            summaries.Add(new ExchangeSummaryResponse(
                value.ExchangeId, value.ExchangeNo, value.OriginalOrderId, value.ChildOrderId,
                value.AmountDue, value.ApproveStatus, value.ExchangeStatus,
                value.ExpireTime, value.CreateTime, value.CompleteTime));
        }

        return OrderTicketResult<PagedExchangeResponse>.Success(
            new PagedExchangeResponse(summaries, query.Page, query.PageSize, total));
    }

    public Task<OrderTicketResult<ExchangeResponse>> GetAsync(
        long userId,
        long exchangeId,
        CancellationToken cancellationToken = default) =>
        LoadResponseAsync(exchangeId, userId, cancellationToken);

    private async Task<OrderTicketResult<ExchangeResponse>> LoadResponseAsync(
        long exchangeId,
        long? userId,
        CancellationToken cancellationToken)
    {
        var requests = dbContext.Set<ExchangeRequest>().AsNoTracking()
            .Include(item => item.AppliedPolicy)
            .Include(item => item.Items).ThenInclude(item => item.OrderItem)!.ThenInclude(item => item!.ETicket)
            .Include(item => item.Items).ThenInclude(item => item.NewOrderItem)!.ThenInclude(item => item!.ETicket)
            .Where(item => item.ExchangeId == exchangeId);
        if (userId.HasValue)
        {
            requests = requests.Where(item => item.UserId == userId.Value);
        }
        var exchange = await requests.SingleOrDefaultAsync(cancellationToken);
        if (exchange is null)
        {
            return NotFound<ExchangeResponse>("EXCHANGE_NOT_FOUND", "The exchange request does not exist.");
        }

        var childIds = exchange.Items.Where(item => item.NewOrderItem is not null)
            .Select(item => item.NewOrderItem!.OrderId).Distinct().ToArray();
        if (exchange.Items.Count == 0 || childIds.Length != 1 ||
            exchange.Items.Any(item => item.OrderItem?.ETicket is null || item.NewOrderItem is null))
        {
            return Conflict<ExchangeResponse>(
                "EXCHANGE_DATA_INCONSISTENT", "The exchange aggregate is incomplete.");
        }
        var child = await dbContext.Set<Order>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.OrderId == childIds[0], cancellationToken);
        if (child is null || child.OrderType != "EXCHANGE" || child.ParentOrderId != exchange.OrderId)
        {
            return Conflict<ExchangeResponse>(
                "EXCHANGE_DATA_INCONSISTENT", "The exchange child order is inconsistent.");
        }

        if (exchange.ExchangeStatus == "COMPLETED" &&
            (exchange.ApproveStatus != "APPROVED" ||
             child.OrderStatus != "ISSUED" ||
             exchange.Items.Any(item =>
                 item.OrderItem?.ItemStatus != "EXCHANGED" ||
                 item.OrderItem.ETicket?.TicketStatus != "EXCHANGED" ||
                 item.NewOrderItem?.ETicket?.TicketStatus != "UNUSED")))
        {
            return Conflict<ExchangeResponse>(
                "EXCHANGE_DATA_INCONSISTENT",
                "The completed exchange aggregate is incomplete.");
        }

        var items = exchange.Items.OrderBy(item => item.ExchangeItemId).Select(item =>
            new ExchangeItemResponse(
                item.ExchangeItemId, item.OrderItemId, item.NewOrderItemId,
                item.NewOrderItem!.SeatId, item.NewOrderItem.PriceStrategyId,
                item.NewOrderItem.RealNameId, item.OrderItem!.UnitPrice, item.NewOrderItem.UnitPrice,
                item.OrderItem.ItemStatus.ToEnum<ShowtimeBackend.Common.OrderItemStatus>(),
                item.OrderItem.ETicket!.TicketStatus.ToEnum<ShowtimeBackend.Common.ETicketStatus>(),
                item.NewOrderItem.ItemStatus.ToEnum<ShowtimeBackend.Common.OrderItemStatus>(),
                item.NewOrderItem.ETicket?.TicketStatus.ToEnum<ShowtimeBackend.Common.ETicketStatus>()))
            .ToList();
        var originalDeduction = items.Sum(item => item.OriginalUnitPrice);
        var targetAmount = items.Sum(item => item.NewUnitPrice);
        if (!IsOracleAmount(originalDeduction) || !IsOracleAmount(targetAmount) ||
            targetAmount - originalDeduction != exchange.PriceDiff ||
            exchange.PriceDiff + exchange.ExchangeFee != child.TotalAmount)
        {
            return Conflict<ExchangeResponse>(
                "EXCHANGE_DATA_INCONSISTENT", "The exchange amount snapshot is inconsistent.");
        }
        return OrderTicketResult<ExchangeResponse>.Success(new ExchangeResponse(
            exchange.ExchangeId, exchange.ExchangeNo, exchange.OrderId, child.OrderId,
            exchange.UserId, exchange.OrigSessionId, exchange.TargetSessionId,
            exchange.ExchangeReason, originalDeduction,
            targetAmount, exchange.PriceDiff, exchange.ExchangeFee,
            child.TotalAmount, exchange.AppliedPolicyId, exchange.AppliedPolicy?.PolicyName,
            exchange.ApproveStatus.ToEnum<ShowtimeBackend.Common.ExchangeApproveStatus>(),
            exchange.ExchangeStatus.ToEnum<ShowtimeBackend.Common.ExchangeStatus>(),
            exchange.ReviewBy, exchange.ReviewTime, exchange.ReviewRemark,
            exchange.CompleteTime, child.ExpireTime, exchange.CreateTime, items));
    }

    private async Task<bool> HasActiveExchangeAsync(
        IReadOnlyCollection<long> originalItemIds,
        CancellationToken cancellationToken) =>
        await dbContext.Set<ExchangeItem>().AsNoTracking()
            .AnyAsync(item =>
                (originalItemIds.Contains(item.OrderItemId) ||
                 originalItemIds.Contains(item.NewOrderItemId)) &&
                item.ExchangeRequest != null &&
                ((item.ExchangeRequest.ApproveStatus == "PENDING" &&
                  item.ExchangeRequest.ExchangeStatus == "PENDING") ||
                 (item.ExchangeRequest.ApproveStatus == "APPROVED" &&
                  item.ExchangeRequest.ExchangeStatus == "PROCESSING")),
                cancellationToken);

    private async Task<bool> HasCompletedExchangeHistoryAsync(
        IReadOnlyCollection<long> originalItemIds,
        CancellationToken cancellationToken) =>
        await dbContext.Set<ExchangeItem>().AsNoTracking()
            .AnyAsync(item =>
                (originalItemIds.Contains(item.OrderItemId) ||
                 originalItemIds.Contains(item.NewOrderItemId)) &&
                item.ExchangeRequest != null &&
                item.ExchangeRequest.ExchangeStatus == "COMPLETED",
                cancellationToken);

    private async Task<bool> HasActiveRefundAsync(
        IReadOnlyCollection<long> originalItemIds,
        CancellationToken cancellationToken) =>
        await dbContext.Set<RefundItem>().AsNoTracking()
            .AnyAsync(item => originalItemIds.Contains(item.OrderItemId) &&
                item.RefundRequest != null &&
                item.RefundRequest.RefundStatus != "FAILED" &&
                item.RefundRequest.RefundStatus != "COMPLETED", cancellationToken);

    private static bool HasDuplicates(IEnumerable<long> values)
    {
        var array = values.ToArray();
        return array.Distinct().Count() != array.Length;
    }

    private static bool IsOracleAmount(decimal value) =>
        value >= 0m && value <= MaxOracleAmount && decimal.Round(value, 2) == value;

    private static string CreateBusinessNumber(string prefix, DateTime now) =>
        $"{prefix}{now:yyyyMMddHHmmssfff}{Guid.NewGuid():N}"[..28].ToUpperInvariant();

    private async Task RollbackAndClearAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
    }

    private static OrderTicketResult<TTarget> CopyFailure<TSource, TTarget>(
        OrderTicketResult<TSource> source) =>
        OrderTicketResult<TTarget>.Fail(source.Failure, source.ErrorCode!, source.Message!);

    private static OrderTicketResult<T> Invalid<T>(string code, string message) =>
        OrderTicketResult<T>.Fail(OrderTicketFailure.InvalidRequest, code, message);

    private static OrderTicketResult<T> NotFound<T>(string code, string message) =>
        OrderTicketResult<T>.Fail(OrderTicketFailure.NotFound, code, message);

    private static OrderTicketResult<T> Conflict<T>(string code, string message) =>
        OrderTicketResult<T>.Fail(OrderTicketFailure.Conflict, code, message);

    private static OrderTicketResult<ExchangeQuoteResponse> Invalid(string code, string message) =>
        OrderTicketResult<ExchangeQuoteResponse>.Fail(OrderTicketFailure.InvalidRequest, code, message);

    private static OrderTicketResult<ExchangeQuoteResponse> NotFound(string code, string message) =>
        OrderTicketResult<ExchangeQuoteResponse>.Fail(OrderTicketFailure.NotFound, code, message);

    private static OrderTicketResult<ExchangeQuoteResponse> Conflict(string code, string message) =>
        OrderTicketResult<ExchangeQuoteResponse>.Fail(OrderTicketFailure.Conflict, code, message);
}
