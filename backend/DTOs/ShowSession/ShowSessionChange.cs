using ShowtimeBackend.Common;

namespace ShowtimeBackend.DTOs.ShowSessionChange;

/// <summary>
/// 创建演出场次请求参数
/// </summary>
public record CreateShowSessionRequest(
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
    PriceType PriceType,   // 取值见 Common.Enums.PriceType（与 DDL CK_PRICE_TYPE 一致）
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
    SessionStatus Status // 取值见 Common.Enums.SessionStatus（与 DDL CK_SHOW_SESSION_STATUS 一致）
);
