namespace ShowtimeBackend.DTOs.OrderTicket;

public sealed record CreateOrderRequest(
    long SessionId,
    IReadOnlyList<CreateOrderItemRequest> Items,
    string? Remark);

/// <summary>
/// 创建订单时提交的单个座位；锁令牌必须与当前用户的有效锁一致。
/// </summary>
public sealed record CreateOrderItemRequest(
    long SeatId,
    long PriceStrategyId,
    long? RealNameId,
    /// <summary>锁座接口为该座位返回的唯一令牌。</summary>
    string LockToken);
