using System.ComponentModel.DataAnnotations;
using ShowtimeBackend.Common;

namespace ShowtimeBackend.DTOs.ShowSessionChange;

/// <summary>
/// 创建/排布场次请求 DTO
/// </summary>
public record CreateShowSessionRequest(
    DateTime StartTime,
    DateTime EndTime,
    DateTime SaleStartTime,
    DateTime SaleEndTime,
    long SeatMapId
);

/// <summary>
/// 配置基础票价策略请求 DTO
/// </summary>
public record CreatePriceStrategyRequest(
    long SeatSectionId,
    PriceType PriceType,
    decimal Price,
    string? StrategyName = null,
    DateTime? SaleStartTime = null,
    DateTime? SaleEndTime = null,
    int Priority = 0,
    int? Quota = null
);

/// <summary>
/// 变更场次状态请求 DTO
/// </summary>
public record UpdateSessionStatusRequest(
    SessionStatus Status
);
