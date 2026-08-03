namespace ShowtimeBackend.Dtos.Admin;

/// <summary>
/// 创建演出场次请求参数
/// </summary>
public record CreateShowSessionRequest(
    long SessionId,
    DateTime StartTime,
    DateTime EndTime,
    DateTime SaleStartTime,
    DateTime SaleEndTime,
    long SeatMapId
);

/// <summary>
/// 设置票价策略请求参数
/// </summary>
public record CreatePriceStrategyRequest(
    long SeatSectionId,
    string PriceType, // 根据我们之前讨论的人物模型设为： "REGULAR", "VIP", "EARLY_BIRD"
    decimal Price
);

/// <summary>
///手动更新场次状态请求参数
/// </summary>
public record UpdateSessionStatusRequest(
    string Status // "PRE_SALE", "ONSALE", "SUSPENDED", "CLOSED"
);
