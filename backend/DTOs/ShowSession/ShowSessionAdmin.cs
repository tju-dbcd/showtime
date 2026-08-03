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
/// 设置票价策略请求参数 (已向 DDL NOT NULL 字段补充完备)
/// </summary>
public record CreatePriceStrategyRequest(
    long SeatSectionId,
    string? StrategyName,  // 策略名称，若为空则后端按 PriceType 自动生成
    string PriceType,      // "EARLY_BIRD", "PRESALE", "STANDARD", "VIP", "MEMBER" 
    decimal Price,
    DateTime? SaleStartTime, // 若为空则默认继承场次的 SaleStartTime
    DateTime? SaleEndTime,   // 若为空则默认继承场次的 SaleEndTime
    int Priority = 0,
    long? Quota = null
);

/// <summary>
/// 手动更新场次状态请求参数
/// </summary>
public record UpdateSessionStatusRequest(
    string Status //  "UPCOMING", "PRESALE", "ONSALE", "SOLD_OUT", "ENDED" 
);
