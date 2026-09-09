using ShowtimeBackend.Entities.ShowSession;

namespace ShowtimeBackend.Services.ShowSession;

/// <summary>
/// 票档生效选择器：为“同一票区同一时刻只生效一档（方案 A）”提供统一判定。
/// </summary>
/// <remarks>
/// 选择规则（优先级从高到低）：
///  1. 状态必须为 ENABLED；
///  2. 售票窗口覆盖评估时刻：SaleStartTime &lt;= evaluationTime &lt;= SaleEndTime；
///     兼容未显式设置窗口的历史数据/直插测试（起止均为 default(DateTime) 时视为全程生效）；
///  3. 多档重叠时取 Priority 最大者；
///  4. 再重叠时按票种档位序取较早/较优惠的档：EARLY_BIRD &lt; PRESALE &lt; STANDARD &lt; VIP &lt; MEMBER；
///  5. 仍并列时取 PriceStrategyId 较小者保证稳定。
/// 展示报价用当前 UTC 时间；下单/改签结算必须用“座位锁创建时间”，与动态调价同口径。
/// </remarks>
public static class PricingTierSelector
{
    private static readonly Dictionary<string, int> PriceTypeRank = new()
    {
        ["EARLY_BIRD"] = 0,
        ["PRESALE"] = 1,
        ["STANDARD"] = 2,
        ["VIP"] = 3,
        ["MEMBER"] = 4,
    };

    public static bool IsActive(PriceStrategy strategy, DateTime evaluationTime)
    {
        if (strategy.Status != "ENABLED") return false;

        // 未显式配置售票窗口（SQLite/测试直插或旧数据）时视为全程生效
        bool windowUnset =
            strategy.SaleStartTime == default(DateTime) &&
            strategy.SaleEndTime == default(DateTime);
        return windowUnset || (strategy.SaleStartTime <= evaluationTime && evaluationTime <= strategy.SaleEndTime);
    }

    /// <summary>
    /// 在指定票区的一组启用策略中选出当前生效档；找不到时返回 null（表示该区域暂无可售票档）。
    /// </summary>
    public static PriceStrategy? SelectEffective(
        IEnumerable<PriceStrategy> strategies,
        long seatSectionId,
        DateTime evaluationTime)
    {
        return strategies
            .Where(s => s.SeatSectionId == seatSectionId && IsActive(s, evaluationTime))
            .OrderByDescending(s => s.Priority)
            .ThenBy(s => PriceTypeRank.TryGetValue(s.PriceType, out var rank) ? rank : int.MaxValue)
            .ThenBy(s => s.PriceStrategyId)
            .FirstOrDefault();
    }
}
