using Microsoft.EntityFrameworkCore;
using Oracle.ManagedDataAccess.Client;
using ShowtimeBackend.Common;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Entities.SeatZone;
using ShowtimeBackend.Entities.ShowSession;
using ShowtimeBackend.Entities.UserPermission;

namespace ShowtimeBackend.Services.OrderTicket;

public sealed class OrderService(AppDbContext dbContext, TimeProvider timeProvider) : IOrderService
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

        // Status 已由 DTO 枚举 + 查询绑定保证合法（取值与 CHK_T_ORDER_STATUS 一致）
        var orders = dbContext.Set<Order>()
            .AsNoTracking()
            .Where(item => item.UserId == userId);
        if (query.Status.HasValue)
        {
            var status = query.Status.Value.ToDbString();
            orders = orders.Where(item => item.OrderStatus == status);
        }

        var totalCount = await orders.CountAsync(cancellationToken);
        // 先物化实体再映射 DTO：字符串状态转枚举在内存中完成（EF 无法在 SQL 中转换）
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
            item.TotalAmount,
            item.DiscountAmount,
            item.TicketCount,
            item.OrderStatus.ToEnum<OrderStatus>(),
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
            item.TotalAmount,
            item.DiscountAmount,
            item.TicketCount,
            item.OrderStatus.ToEnum<OrderStatus>(),
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
        CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (request.SessionId <= 0 || request.Items.Count is 0 or > MaxSeatsPerOrder ||
            request.Items.Any(item => item.SeatId <= 0 ||
                                      item.PriceStrategyId <= 0 ||
                                      string.IsNullOrWhiteSpace(item.LockToken) ||
                                      item.LockToken.Length > 64) ||
            request.Items.Select(item => item.SeatId).Distinct().Count() != request.Items.Count ||
            request.Items.Select(item => item.LockToken)
                .Distinct(StringComparer.Ordinal).Count() != request.Items.Count)
        {
            return Invalid("ORDER_INVALID_ITEMS", "Order items must contain valid, distinct seats.");
        }

        if (await dbContext.Set<ShowtimeBackend.Entities.ShowSession.ShowSession>()
                .AsNoTracking()
                .CountAsync(item => item.SessionId == request.SessionId, cancellationToken) == 0)
        {
            return NotFound("ORDER_SESSION_NOT_FOUND", "The requested session does not exist.");
        }

        var seatIds = request.Items.Select(item => item.SeatId).ToArray();
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

        var orderItems = new List<OrderItem>(request.Items.Count);
        foreach (var requestedItem in request.Items)
        {
            if (!seats.TryGetValue(requestedItem.SeatId, out var seat) ||
                !seat.IsSellable || seat.SeatStatus != "ENABLED")
            {
                return Invalid(
                    "ORDER_SEAT_UNAVAILABLE",
                    $"Seat {requestedItem.SeatId} is unavailable.");
            }

            if (!strategies.TryGetValue(requestedItem.PriceStrategyId, out var strategy) ||
                strategy.SessionId != request.SessionId ||
                strategy.SeatSectionId != seat.SeatSectionId ||
                strategy.Status != "ENABLED")
            {
                return Invalid(
                    "ORDER_INVALID_PRICE_STRATEGY",
                    $"Price strategy {requestedItem.PriceStrategyId} cannot price seat {requestedItem.SeatId}.");
            }

            orderItems.Add(new OrderItem
            {
                SeatId = seat.SeatId,
                PriceStrategyId = strategy.PriceStrategyId,
                RealNameId = requestedItem.RealNameId,
                UnitPrice = strategy.Price,
                ItemStatus = "NORMAL",
                CreateBy = actor,
                UpdateBy = actor
            });
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        // 下单人、场次、座位和令牌必须同时匹配，不能使用其他用户或旧页面留下的锁。
        var locks = await dbContext.SeatLocks
            .Where(item => item.SessionId == request.SessionId &&
                           item.UserId == userId &&
                           seatIds.Contains(item.SeatId) &&
                           item.LockStatus == "ACTIVE" &&
                           item.ExpireTime > now)
            .ToDictionaryAsync(item => item.SeatId, cancellationToken);
        if (locks.Count != request.Items.Count || request.Items.Any(item =>
                !locks.TryGetValue(item.SeatId, out var seatLock) ||
                !string.Equals(
                    seatLock.LockToken,
                    item.LockToken,
                    StringComparison.Ordinal)))
        {
            return OrderTicketResult<OrderResponse>.Fail(
                OrderTicketFailure.Conflict,
                "ORDER_SEAT_LOCK_INVALID",
                "Every order item requires an active seat lock owned by the current user.");
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
            ExpireTime = now.AddMinutes(15),
            Source = "WEB",
            Remark = string.IsNullOrWhiteSpace(request.Remark) ? null : request.Remark.Trim(),
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
                // 在数据库中以条件更新消费锁，确保释放和重复下单只有一个操作能够成功。
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
                    await transaction!.RollbackAsync(cancellationToken);
                    return OrderTicketResult<OrderResponse>.Fail(
                        OrderTicketFailure.Conflict,
                        "ORDER_SEAT_LOCK_INVALID",
                        "One or more seat locks are no longer active.");
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

            // 首次保存后订单明细获得主键，才能建立订单明细与正式占座记录的对应关系。
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
        catch (DbUpdateException exception) when (ContainsOracleError(exception, 1))
        {
            // 活动预留唯一索引是防止同一场次、同一座位重复下单的最后一道保护。
            return OrderTicketResult<OrderResponse>.Fail(
                OrderTicketFailure.Conflict,
                "ORDER_SEAT_UNAVAILABLE",
                "One or more seats have already been reserved.");
        }

        return OrderTicketResult<OrderResponse>.Success(ToResponse(order));
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

        // 订单取消后同步取消正式占座，座位才能再次参与锁座。
        foreach (var reservation in reservations)
        {
            reservation.ReservationStatus = "CANCELLED";
            reservation.CancelTime = now;
            reservation.UpdateBy = actor;
        }

        try
        {
            // OrderStatus 是并发令牌；支付和取消竞争时只有先提交的一方成功。
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

    private static string CreateBusinessNumber(string prefix, DateTime now) =>
        $"{prefix}{now:yyyyMMddHHmmssfff}{Guid.NewGuid():N}"[..28].ToUpperInvariant();

    private static OrderResponse ToResponse(Order order) => new(
        order.OrderId,
        order.OrderNo,
        order.SessionId,
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

    private static bool ContainsOracleError(Exception exception, int number)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is OracleException oracleException && oracleException.Number == number)
            {
                return true;
            }
        }

        return false;
    }
}
