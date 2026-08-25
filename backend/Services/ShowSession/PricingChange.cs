using System;
using System.Collections.Generic;
using System.Linq;
using ShowtimeBackend.Entities.ShowSession;

namespace ShowtimeBackend.Services.ShowSession;

public static class PricingChange
{
    /// <summary>
    /// 根据基础策略与动态调价规则，计算当前时间点的最终生效票价
    /// </summary>
    public static decimal CalculateRealtimePrice(
        decimal basePrice,
        DateTime sessionStartTime,
        DateTime nowUtc,
        long seatSectionId,
        IEnumerable<DynamicPricingRule> rules)
    {
        var minutesToStart = (int)(sessionStartTime - nowUtc).TotalMinutes;

        // 筛选作用于该看台且处于启用状态的规则，按优先级降序排序
        var matchedRule = rules
            .Where(r => r.Status == "ENABLED")
            .Where(r => r.SeatSectionId == null || r.SeatSectionId == seatSectionId)
            .Where(r => MatchesTimeWindow(r, minutesToStart))
            .OrderByDescending(r => r.Priority)
            .FirstOrDefault();

        if (matchedRule == null) return basePrice;

        return matchedRule.AdjustmentType switch
        {
            "DISCOUNT_RATE" => Math.Round(basePrice * matchedRule.AdjustmentValue, 2, MidpointRounding.AwayFromZero),
            "AMOUNT_OFF" => Math.Max(0, basePrice - matchedRule.AdjustmentValue),
            "FIXED_PRICE" => matchedRule.AdjustmentValue,
            _ => basePrice
        };
    }

    public static bool IsRuleTriggered(DynamicPricingRule rule, DateTime sessionStartTime, DateTime nowUtc)
    {
        if (rule.Status != "ENABLED") return false;

        return rule.TriggerType switch
        {
            "TIME_WINDOW" => EvaluateTimeWindowRule(rule, sessionStartTime, nowUtc),
            "INVENTORY_RATE" => false, 
            _ => false
        };
    }

    private static bool MatchesTimeWindow(DynamicPricingRule rule, int minutesToStart)
    {
        if (rule.TriggerType != "TIME_WINDOW") return true;

        bool startMatch = !rule.StartOffsetMinutes.HasValue || minutesToStart <= rule.StartOffsetMinutes.Value;
        bool endMatch = !rule.EndOffsetMinutes.HasValue || minutesToStart >= rule.EndOffsetMinutes.Value;

        return startMatch && endMatch;
    }
}
