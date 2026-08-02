namespace ShowtimeBackend.DTOs.OrderTicket;

public sealed record CreateOrderRequest(
    long SessionId,
    IReadOnlyList<CreateOrderItemRequest> Items,
    string? Remark);

public sealed record CreateOrderItemRequest(
    long SeatId,
    long PriceStrategyId,
    long? RealNameId);
