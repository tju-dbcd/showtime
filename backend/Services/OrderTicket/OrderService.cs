using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ShowtimeBackend.Common;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.DTOs.ShowSessionChange;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Entities.SeatZone;
using ShowtimeBackend.Entities.ShowSession;
using ShowtimeBackend.Entities.UserPermission;
using ShowtimeBackend.Services.SeatZone;
using ShowtimeBackend.Services.ShowSession;

namespace ShowtimeBackend.Services.OrderTicket;

public sealed class OrderService(
    AppDbContext dbContext,
    TimeProvider timeProvider,
    ISeatLockGuard? seatLockGuard = null,
    IOptions<OrderExpirationOptions>? expirationOptions = null) : IOrderService
{
    // 与座位规则 NUMBER(3) 的取值范围保持一致，并避免生成过大的 Oracle IN 查询。
    private const int MaxSeatsPerOrder = 999;

    public async Task<OrderTicketResult<PagedOrderResponse>> ListAsync(
        long userId,
        OrderListQuery query,
        CancellationToken cancellationToken)
    {
        if (query.Page < 1 || query.PageSize is < 1 or > 100)
        {
            return OrderTicketResult<PagedOrderResponse>.Fail(
                OrderTicketFailure.InvalidRequest,
                "ORDER_INVALID_PAGING",
                "Page must be positive and pageSize must be between 1 and 100.");
        }

        var orders = dbContext.Set<Order>()
            .AsNoTracking()
            .Where(item => item.UserId == userId);
        if (query.Status.HasValue)
        {
            var status = query.Status.Value.ToDbString();
            orders = orders.Where(item => item.OrderStatus == status);
        }

        var totalCount = await orders.CountAsync(cancellationToken);
        var entities = await orders
            .OrderByDescending(item => item.CreateTime)
            .ThenByDescending(item => item.OrderId)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);
        var items = entities.Select(item => new OrderSummaryResponse(
            item.OrderId,
            item.OrderNo,
            item.SessionId,
            item.OrderType.ToEnum<OrderType>(),
            item.ParentOrderId,
            item.TotalAmount,
            item.DiscountAmount,
            item.TicketCount,
            item.OrderStatus.ToEnum<OrderStatus>(),
            item.OrderType != "EXCHANGE" && item.OrderStatus == "PENDING_PAY",
            item.OrderType != "EXCHANGE" && item.OrderStatus == "PENDING_PAY",
            item.ExpireTime,
            item.CreateTime)).ToList();

        return OrderTicketResult<PagedOrderResponse>.Success(
            new PagedOrderResponse(items, query.Page, query.PageSize, totalCount));
    }

    public async Task<OrderTicketResult<OrderResponse>> GetAsync(
        long userId,
        long orderId,
        CancellationToken cancellationToken)
    {
        var order = await FindOrderDetailsAsync(orderId, userId, cancellationToken);

        return order is null
            ? NotFound("ORDER_NOT_FOUND", "The order does not exist.")
            : OrderTicketResult<OrderResponse>.Success(ToResponse(order));
    }

    public async Task<OrderTicketResult<PagedAdminOrderResponse>> ListAdminAsync(
        AdminOrderListQuery query,
        CancellationToken cancellationToken)
    {
        var offset = ((long)query.Page - 1) * query.PageSize;
        if (query.Page < 1 || query.PageSize is < 1 or > 100 || offset > int.MaxValue)
        {
            return OrderTicketResult<PagedAdminOrderResponse>.Fail(
                OrderTicketFailure.InvalidRequest,
                "ORDER_INVALID_PAGING",
                "Page must be positive and pageSize must be between 1 and 100.");
        }

        var orders = dbContext.Set<Order>()
            .AsNoTracking()
            .Include(item => item.User)
            .AsQueryable();
        if (query.Status.HasValue)
        {
            var status = query.Status.Value.ToDbString();
            orders = orders.Where(item => item.OrderStatus == status);
        }

        var keyword = query.Keyword?.Trim();
        if (!string.IsNullOrEmpty(keyword))
        {
            orders = orders.Where(item =>
                item.OrderNo.Contains(keyword) ||
                item.User != null &&
                (item.User.UserName.Contains(keyword) ||
                 item.User.Nickname != null && item.User.Nickname.Contains(keyword) ||
                 item.User.Phone.Contains(keyword)));
        }

        var totalCount = await orders.CountAsync(cancellationToken);
        var entities = await orders
            .OrderByDescending(item => item.CreateTime)
            .ThenByDescending(item => item.OrderId)
            .Skip((int)offset)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);
        var items = entities.Select(item => new AdminOrderSummaryResponse(
            item.OrderId,
            item.OrderNo,
            item.UserId,
            item.User!.UserName,
            item.User.Nickname,
            item.User.Phone,
            item.SessionId,
            item.OrderType.ToEnum<OrderType>(),
            item.ParentOrderId,
            item.TotalAmount,
            item.DiscountAmount,
            item.TicketCount,
            item.OrderStatus.ToEnum<OrderStatus>(),
            item.OrderType != "EXCHANGE" && item.OrderStatus == "PENDING_PAY",
            item.OrderType != "EXCHANGE" && item.OrderStatus == "PENDING_PAY",
            item.ExpireTime,
            item.CreateTime)).ToList();

        return OrderTicketResult<PagedAdminOrderResponse>.Success(
            new PagedAdminOrderResponse(items, query.Page, query.PageSize, totalCount));
    }

    public async Task<OrderTicketResult<OrderResponse>> GetAdminAsync(
        long orderId,
        CancellationToken cancellationToken)
    {
        var order = await FindOrderDetailsAsync(orderId, null, cancellationToken);
        return order is null
            ? NotFound("ORDER_NOT_FOUND", "The order does not exist.")
            : OrderTicketResult<OrderResponse>.Success(ToResponse(order));
    }

    public async Task<OrderTicketResult<OrderResponse>> CreateAsync(
        long userId,
        string actor,
        string? idempotencyKey,
        CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedIdempotencyKey = idempotencyKey?.Trim();
        if (string.IsNullOrEmpty(normalizedIdempotencyKey) ||
            normalizedIdempotencyKey.Length > 64)
        {
            return Invalid(
                "ORDER_INVALID_IDEMPOTENCY_KEY",
                "Idempotency-Key is required and must not exceed 64 characters after trimming.");
        }

        var normalizedItems = request.Items.Select(item => new OrderIdempotencyItem(
            item.SeatId,
            item.PriceStrategyId,
            item.RealNameId,
            item.LockToken ?? string.Empty)).ToArray();
        if (request.SessionId <= 0 || request.Items.Count is 0 or > MaxSeatsPerOrder ||
            normalizedItems.Any(item => item.SeatId <= 0 ||
                                        item.PriceStrategyId <= 0 ||
                                        string.IsNullOrWhiteSpace(item.LockToken) ||
                                        item.LockToken.Length > 64) ||
            normalizedItems.Select(item => item.SeatId).Distinct().Count() != request.Items.Count ||
            normalizedItems.Select(item => item.LockToken)
                .Distinct(StringComparer.Ordinal).Count() != request.Items.Count)
        {
            return Invalid("ORDER_INVALID_ITEMS", "Order items must contain valid, distinct seats.");
        }

        var normalizedRemark = string.IsNullOrWhiteSpace(request.Remark)
            ? null
            : request.Remark.Trim();
        var requestHash = OrderIdempotencyRequestHasher.Compute(
            request.SessionId,
            normalizedItems,
            normalizedRemark);
        var existing = await FindIdempotencyRecordAsync(
            userId,
            normalizedIdempotencyKey,
            cancellationToken);
        if (existing is not null)
        {
            return await ResolveIdempotencyRecordAsync(
                existing,
                requestHash,
                userId,
                cancellationToken);
        }

        // 查询场次实体
        var session = await dbContext.Set<ShowtimeBackend.Entities.ShowSession.ShowSession>()
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.SessionId == request.SessionId, cancellationToken);

        if (session is null)
        {
            return NotFound("ORDER_SESSION_NOT_FOUND", "The requested session does not exist.");
        }

        // 查询当前场次生效中的动态调价规则
        var dynamicRules = await dbContext.Set<DynamicPricingRule>()
            .AsNoTracking()
            .Where(r => r.SessionId == request.SessionId && r.Status == "ENABLED")
            .ToListAsync(cancellationToken);

        var seatIds = request.Items.Select(item => item.SeatId).ToArray();
        var lockTokens = normalizedItems.ToDictionary(
            item => item.SeatId,
            item => item.LockToken);
        var seats = await dbContext.Set<Seat>()
            .AsNoTracking()
            .Where(item => seatIds.Contains(item.SeatId))
            .ToDictionaryAsync(item => item.SeatId, cancellationToken);
        var strategyIds = request.Items.Select(item => item.PriceStrategyId).Distinct().ToArray();
        var strategies = await dbContext.Set<PriceStrategy>()
            .AsNoTracking()
            .Where(item => strategyIds.Contains(item.PriceStrategyId))
            .ToDictionaryAsync(item => item.PriceStrategyId, cancellationToken);

        var realNameIds = request.Items
            .Where(item => item.RealNameId.HasValue)
            .Select(item => item.RealNameId!.Value)
            .Distinct()
            .ToArray();
        if (realNameIds.Length > 0)
        {
            var validRealNameCount = await dbContext.Set<UserRealName>()
                .CountAsync(
                    item => realNameIds.Contains(item.RealNameId) &&
                            item.UserId == userId &&
                            item.IsVerified,
                    cancellationToken);
            if (validRealNameCount != realNameIds.Length)
            {
                return Invalid(
                    "ORDER_INVALID_REAL_NAME",
                    "A verified real-name record owned by the user is required.");
            }
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        // 查出用户锁记录，但不在此处直接阻断，保证座位和策略校验可以按预期的优先顺序触发
        var locks = await dbContext.SeatLocks
            .Where(item => item.SessionId == request.SessionId &&
                           item.UserId == userId &&
                           seatIds.Contains(item.SeatId) &&
                           item.LockStatus == "ACTIVE" &&
                           item.ExpireTime > now)
            .ToDictionaryAsync(item => item.SeatId, cancellationToken);

        var orderItems = new List<OrderItem>(request.Items.Count);
        foreach (var requestedItem in request.Items)
        {
            // 先校验座位可用性
            if (!seats.TryGetValue(requestedItem.SeatId, out var seat) ||
                !seat.IsSellable || seat.SeatStatus != "ENABLED")
            {
                return Invalid(
                    "ORDER_SEAT_UNAVAILABLE",
                    $"Seat {requestedItem.SeatId} is unavailable.");
            }

            // 再校验价格策略有效性
            if (!strategies.TryGetValue(requestedItem.PriceStrategyId, out var strategy) ||
                strategy.SessionId != request.SessionId ||
                strategy.SeatSectionId != seat.SeatSectionId ||
                strategy.Status != "ENABLED")
            {
                return Invalid(
                    "ORDER_INVALID_PRICE_STRATEGY",
                    $"Price strategy {requestedItem.PriceStrategyId} cannot price seat {requestedItem.SeatId}.");
            }

            // 优先取锁创建时间计价，若无锁记录（测试场景）回退到当前时间
            var lockTime = locks.TryGetValue(requestedItem.SeatId, out var seatLock)
                ? seatLock.CreateTime
                : now;

            decimal realtimeUnitPrice = PricingChange.CalculateRealtimePrice(
                strategy.Price,
                session.StartTime,
                lockTime,
                strategy.SeatSectionId,
                dynamicRules);

            orderItems.Add(new OrderItem
            {
                SeatId = seat.SeatId,
                PriceStrategyId = strategy.PriceStrategyId,
                RealNameId = requestedItem.RealNameId,
                UnitPrice = realtimeUnitPrice,
                ItemStatus = "NORMAL",
                CreateBy = actor,
                UpdateBy = actor
            });
        }

        // 座位与策略通过校验后，校验锁完整性
        if (locks.Count != request.Items.Count || request.Items.Any(item =>
                !locks.TryGetValue(item.SeatId, out var seatLock) ||
                !string.Equals(
                    seatLock.LockToken,
                    lockTokens[item.SeatId],
                    StringComparison.Ordinal)))
        {
            var fallback = OrderTicketResult<OrderResponse>.Fail(
                OrderTicketFailure.Conflict,
                "ORDER_SEAT_LOCK_INVALID",
                "Every order item requires an active seat lock owned by the current user.");
            return await RecoverIdempotencyAsync(
                userId,
                normalizedIdempotencyKey,
                requestHash,
                fallback,
                missingWinnerIsFailure: false,
                cancellationToken);
        }

        var order = new Order
        {
            OrderNo = CreateBusinessNumber("ORD", now),
            UserId = userId,
            SessionId = request.SessionId,
            OrderType = "NORMAL",
            TotalAmount = orderItems.Sum(item => item.UnitPrice),
            DiscountAmount = 0m,
            TicketCount = orderItems.Count,
            OrderStatus = "PENDING_PAY",
            ExpireTime = now.AddMinutes(
                expirationOptions?.Value.PendingPaymentExpireMinutes ??
                new OrderExpirationOptions().PendingPaymentExpireMinutes),
            Source = "WEB",
            Remark = normalizedRemark,
            IdempotencyKey = normalizedIdempotencyKey,
            IdempotencyRequestHash = requestHash,
            CreateBy = actor,
            UpdateBy = actor,
            Items = orderItems
        };

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            if (dbContext.Database.IsRelational())
            {
                var lockIds = locks.Values.Select(item => item.SeatLockId).ToArray();
                var convertedCount = await dbContext.SeatLocks
                    .Where(item => lockIds.Contains(item.SeatLockId) &&
                                   item.UserId == userId &&
                                   item.LockStatus == "ACTIVE" &&
                                   item.ExpireTime > now)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(item => item.LockStatus, "CONVERTED")
                        .SetProperty(item => item.UpdateBy, actor),
                        cancellationToken);

                if (convertedCount != request.Items.Count)
                {
                    await RollbackAsync(transaction, cancellationToken);
                    var fallback = OrderTicketResult<OrderResponse>.Fail(
                        OrderTicketFailure.Conflict,
                        "ORDER_SEAT_LOCK_INVALID",
                        "One or more seat locks are no longer active.");
                    return await RecoverIdempotencyAsync(
                        userId,
                        normalizedIdempotencyKey,
                        requestHash,
                        fallback,
                        missingWinnerIsFailure: false,
                        cancellationToken);
                }
            }
            else
            {
                foreach (var seatLock in locks.Values)
                {
                    seatLock.LockStatus = "CONVERTED";
                    seatLock.UpdateBy = actor;
                }
            }

            dbContext.Add(order);
            await dbContext.SaveChangesAsync(cancellationToken);

            for (var index = 0; index < orderItems.Count; index++)
            {
                var orderItem = orderItems[index];
                var requestedItem = request.Items[index];
                var seatLock = locks[requestedItem.SeatId];

                dbContext.SeatReservations.Add(new SeatReservation
                {
                    SessionId = request.SessionId,
                    SeatId = requestedItem.SeatId,
                    OrderItemId = orderItem.OrderItemId,
                    SeatLockId = seatLock.SeatLockId,
                    ReservationType = "ORDER",
                    ReservationStatus = "ACTIVE",
                    ReserveTime = now,
                    CreateBy = actor,
                    UpdateBy = actor
                });
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (DbUpdateException exception)
        {
            await RollbackAsync(transaction, cancellationToken);

            var uniqueConstraint = OrderCreateConstraintClassifier.Classify(exception);
            if (uniqueConstraint == OrderCreateUniqueConstraint.IdempotencyKey)
            {
                return await RecoverIdempotencyAsync(
                    userId,
                    normalizedIdempotencyKey,
                    requestHash,
                    fallback: null,
                    missingWinnerIsFailure: true,
                    cancellationToken);
            }

            if (uniqueConstraint == OrderCreateUniqueConstraint.SeatReservation)
            {
                return OrderTicketResult<OrderResponse>.Fail(
                    OrderTicketFailure.Conflict,
                    "ORDER_SEAT_UNAVAILABLE",
                    "One or more seats have already been reserved.");
            }

            throw;
        }
        catch (Exception)
        {
            await RollbackAsync(transaction, cancellationToken);

            throw;
        }

        // DB 侧锁已全部 CONVERTED（事务已提交/内存库保存成功），释放 Redis 座位锁，避免残留 key 阻塞后续锁座。
        await ReleaseGuardKeysAsync(request.SessionId, locks.Values);

        return OrderTicketResult<OrderResponse>.Success(ToResponse(order));
    }

    /// <summary>下单转换成功后释放订单项对应的 Redis 座位锁（按 token 比对防误删；失败由 TTL 兜底）。</summary>
    private async Task ReleaseGuardKeysAsync(
        long sessionId,
        IEnumerable<SeatLock> locks)
    {
        if (seatLockGuard is null)
        {
            return;
        }

        foreach (var seatLock in locks)
        {
            await seatLockGuard.ReleaseAsync(
                sessionId, seatLock.SeatId, seatLock.LockToken);
        }
    }

    public async Task<OrderTicketResult<OrderResponse>> CancelAsync(
        long userId,
        string actor,
        long orderId,
        CancellationToken cancellationToken)
    {
        var order = await dbContext.Set<Order>()
            .Include(item => item.Items)
            .SingleOrDefaultAsync(item => item.OrderId == orderId && item.UserId == userId, cancellationToken);
        if (order is null)
        {
            return NotFound("ORDER_NOT_FOUND", "The order does not exist.");
        }

        return await CancelOrderAsync(order, actor, cancellationToken);
    }

    public async Task<OrderTicketResult<OrderResponse>> CancelAdminAsync(
        string actor,
        long orderId,
        CancellationToken cancellationToken)
    {
        var order = await dbContext.Set<Order>()
            .Include(item => item.Items)
            .SingleOrDefaultAsync(item => item.OrderId == orderId, cancellationToken);
        if (order is null)
        {
            return NotFound("ORDER_NOT_FOUND", "The order does not exist.");
        }

        return await CancelOrderAsync(order, actor, cancellationToken);
    }

    private async Task<OrderTicketResult<OrderResponse>> CancelOrderAsync(
        Order order,
        string actor,
        CancellationToken cancellationToken)
    {
        if (order.OrderType == "EXCHANGE")
        {
            return OrderTicketResult<OrderResponse>.Fail(
                OrderTicketFailure.Conflict,
                "EXCHANGE_CANCEL_REQUIRES_WORKFLOW",
                "Exchange child orders must be cancelled through the exchange workflow.");
        }

        if (order.OrderStatus != "PENDING_PAY")
        {
            return OrderTicketResult<OrderResponse>.Fail(
                OrderTicketFailure.Conflict,
                "ORDER_CANNOT_CANCEL",
                "Only pending-payment orders can be cancelled.");
        }

        var orderItemIds = order.Items.Select(item => item.OrderItemId).ToArray();
        var reservations = await dbContext.SeatReservations
            .Where(item => item.OrderItemId.HasValue &&
                           orderItemIds.Contains(item.OrderItemId.Value) &&
                           item.ReservationStatus == "ACTIVE")
            .ToListAsync(cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        order.OrderStatus = "CANCELLED";
        order.CancelTime = now;
        order.UpdateBy = actor;

        foreach (var reservation in reservations)
        {
            reservation.ReservationStatus = "CANCELLED";
            reservation.CancelTime = now;
            reservation.UpdateBy = actor;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return OrderTicketResult<OrderResponse>.Fail(
                OrderTicketFailure.Conflict,
                "ORDER_CANNOT_CANCEL",
                "The order status changed and it can no longer be cancelled.");
        }

        return OrderTicketResult<OrderResponse>.Success(ToResponse(order));
    }

    private async Task<Order?> FindOrderDetailsAsync(
        long orderId,
        long? userId,
        CancellationToken cancellationToken)
    {
        var orders = dbContext.Set<Order>()
            .AsNoTracking()
            .Include(item => item.Items)
            .ThenInclude(item => item.ETicket)
            .Include(item => item.Payments)
            .AsQueryable();
        if (userId.HasValue)
        {
            orders = orders.Where(item => item.UserId == userId.Value);
        }

        return await orders.SingleOrDefaultAsync(item => item.OrderId == orderId, cancellationToken);
    }

    private Task<IdempotencyRecord?> FindIdempotencyRecordAsync(
        long userId,
        string idempotencyKey,
        CancellationToken cancellationToken) => dbContext.Set<Order>()
        .AsNoTracking()
        .Where(item =>
            item.UserId == userId &&
            item.IdempotencyKey == idempotencyKey)
        .Select(item => new IdempotencyRecord(
            item.OrderId,
            item.IdempotencyRequestHash))
        .SingleOrDefaultAsync(cancellationToken);

    private async Task<OrderTicketResult<OrderResponse>> RecoverIdempotencyAsync(
        long userId,
        string idempotencyKey,
        string requestHash,
        OrderTicketResult<OrderResponse>? fallback,
        bool missingWinnerIsFailure,
        CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        var existing = await FindIdempotencyRecordAsync(
            userId,
            idempotencyKey,
            cancellationToken);
        if (existing is not null)
        {
            return await ResolveIdempotencyRecordAsync(
                existing,
                requestHash,
                userId,
                cancellationToken);
        }

        if (!missingWinnerIsFailure && fallback is not null)
            return fallback;

        return OrderTicketResult<OrderResponse>.Fail(
            OrderTicketFailure.Internal,
            "ORDER_IDEMPOTENCY_RECOVERY_FAILED",
            "The competing idempotent order could not be recovered.");
    }

    private async Task<OrderTicketResult<OrderResponse>> ResolveIdempotencyRecordAsync(
        IdempotencyRecord existing,
        string requestHash,
        long userId,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                existing.RequestHash,
                requestHash,
                StringComparison.Ordinal))
        {
            return OrderTicketResult<OrderResponse>.Fail(
                OrderTicketFailure.Conflict,
                "ORDER_IDEMPOTENCY_CONFLICT",
                "Idempotency-Key was already used for a different order request.");
        }

        var order = await FindOrderDetailsAsync(
            existing.OrderId,
            userId,
            cancellationToken);
        return order is null
            ? OrderTicketResult<OrderResponse>.Fail(
                OrderTicketFailure.Internal,
                "ORDER_IDEMPOTENCY_RECOVERY_FAILED",
                "The idempotent order could not be loaded.")
            : OrderTicketResult<OrderResponse>.Success(ToResponse(order));
    }

    private static async Task RollbackAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is null)
            return;

        await transaction.RollbackAsync(cancellationToken);
        await transaction.DisposeAsync();
    }

    private static string CreateBusinessNumber(string prefix, DateTime now) =>
        $"{prefix}{now:yyyyMMddHHmmssfff}{Guid.NewGuid():N}"[..28].ToUpperInvariant();

    private static OrderResponse ToResponse(Order order) => new(
        order.OrderId,
        order.OrderNo,
        order.SessionId,
        order.OrderType.ToEnum<OrderType>(),
        order.ParentOrderId,
        order.TotalAmount,
        order.DiscountAmount,
        order.TicketCount,
        order.OrderStatus.ToEnum<OrderStatus>(),
        order.ExpireTime,
        order.PayTime,
        order.IssueTime,
        order.CancelTime,
        order.Source,
        order.Remark,
        order.OrderType != "EXCHANGE" && order.OrderStatus == "PENDING_PAY",
        order.OrderType != "EXCHANGE" && order.OrderStatus == "PENDING_PAY",
        order.Items.Select(item => new OrderItemResponse(
            item.OrderItemId,
            item.SeatId,
            item.PriceStrategyId,
            item.RealNameId,
            item.UnitPrice,
            item.ItemStatus.ToEnum<OrderItemStatus>())).ToList(),
        order.Payments.Select(item => new PaymentResponse(
            item.PaymentId,
            item.PaymentNo,
            item.OrderId,
            item.PayAmount,
            item.PayChannel.ToEnum<PaymentChannel>(),
            item.PayStatus.ToEnum<PaymentStatus>(),
            item.TradeNo,
            item.CallbackTime,
            item.PayTime)).ToList(),
        order.Items
            .Where(item => item.ETicket is not null)
            .Select(item => new ETicketSummaryResponse(
                item.ETicket!.ETicketId,
                item.ETicket.ETicketNo,
                item.ETicket.OrderItemId,
                item.ETicket.TicketStatus.ToEnum<ETicketStatus>()))
            .ToList(),
        order.CreateTime);

    private static OrderTicketResult<OrderResponse> Invalid(string code, string message) =>
        OrderTicketResult<OrderResponse>.Fail(OrderTicketFailure.InvalidRequest, code, message);

    private static OrderTicketResult<OrderResponse> NotFound(string code, string message) =>
        OrderTicketResult<OrderResponse>.Fail(OrderTicketFailure.NotFound, code, message);

    private sealed record IdempotencyRecord(long OrderId, string? RequestHash);
}
