namespace ShowtimeBackend.DTOs.ShowSessionDto
{
    /// <summary>
    /// 实现演出场次的基本信息传输对象
    /// </summary>>
    public record ShowSessionDto(long ShowId, long SessionId, DateTime StartTime, DateTime EndTime, DateTime SaleStartTime, string SessionStatus, long SeatMapId);

    /// <summary>
    /// 实现演出场次的票价策略传输对象
    /// </summary>
    /// <param name="PriceStrategyId"></param> 票价策略主键 ID
    /// <param name="SeatSectionId"></param> 座位区域主键 ID
    /// <param name="PriceType"></param> 票价类型（VIP票等）
    /// <param name="Price"></param> 票价金额
    /// <param name="Status"></param> 票价策略状态
    public record PricingStrategyDto(long PriceStrategyId, long SeatSectionId, string PriceType, decimal Price, string Status);
}


