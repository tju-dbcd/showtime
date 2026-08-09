using Microsoft.EntityFrameworkCore;
using Oracle.ManagedDataAccess.Client;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Entities.SeatZone;
using ShowtimeBackend.Entities.ShowSession;
using ShowtimeBackend.Entities.UserPermission;

namespace ShowtimeBackend.Services.OrderTicket;

public sealed class OrderService(AppDbContext dbContext, TimeProvider timeProvider) : IOrderService
{
    private const int MaxSeatsPerOrder = 999;
    private static readonly HashSet<string> OrderStatuses =
    [
        "PENDING_PAY", "PAID", "ISSUED", "PART_REFUND", "REFUNDED", "CANCELLED"
    ];

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

        var status = string.IsNullOrWhiteSpace(query.Status)
            ? null
            : query.Status.Trim().ToUpperInvariant();
        if (status is not null && !OrderStatuses.Contains(status))
        {
            return OrderTicketResult<PagedOrderResponse>.Fail(
                OrderTicketFailure.InvalidRequest,
                "ORDER_INVALID_STATUS",
                "The requested order status is invalid.");
        }

        var orders = dbContext.Set<Order>()
            .AsNoTracking()
            .Where(item => item.UserId == userId);
        if (status is not null)
        {
            orders = orders.Where(item => item.OrderStatus == status);
        }

        var totalCount = await orders.CountAsync(cancellationToken);
        var items = await orders
            .OrderByDescending(item => item.CreateTime)
            .ThenByDescending(item => item.OrderId)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(item => new OrderSummaryResponse(
                item.OrderId,
                item.OrderNo,
                item.SessionId,
                item.TotalAmount,
                item.DiscountAmount,
                item.TicketCount,
                item.OrderStatus,
                item.ExpireTime,
                item.CreateTime))
            .ToListAsync(cancellationToken);

        return OrderTicketResult<PagedOrderResponse>.Success(
            new PagedOrderResponse(items, query.Page, query.PageSize, totalCount));
    }

    public async Task<OrderTicketResult<OrderResponse>> GetAsync(
        long userId,
        long orderId,
        CancellationToken cancellationToken)
    {
        var order = await dbContext.Set<Order>()
            .AsNoTracking()
            .Include(item => item.Items)
            .ThenInclude(item => item.ETicket)
            .Include(item => item.Payments)
            .SingleOrDefaultAsync(item => item.OrderId == orderId && item.UserId == userId, cancellationToken);

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

        await dbContext.SaveChangesAsync(cancellationToken);

        return OrderTicketResult<OrderResponse>.Success(ToResponse(order));
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
        order.OrderStatus,
        order.ExpireTime,
        order.PayTime,
        order.CancelTime,
        order.Source,
        order.Remark,
        order.Items.Select(item => new OrderItemResponse(
            item.OrderItemId,
            item.SeatId,
            item.PriceStrategyId,
            item.RealNameId,
            item.UnitPrice,
            item.ItemStatus)).ToList(),
        order.Payments.Select(item => new PaymentResponse(
            item.PaymentId,
            item.PaymentNo,
            item.OrderId,
            item.PayAmount,
            item.PayChannel,
            item.PayStatus,
            item.TradeNo,
            item.CallbackTime,
            item.PayTime)).ToList(),
        order.Items
            .Where(item => item.ETicket is not null)
            .Select(item => new ETicketSummaryResponse(
                item.ETicket!.ETicketId,
                item.ETicket.ETicketNo,
                item.ETicket.OrderItemId,
                item.ETicket.TicketStatus))
            .ToList());

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
