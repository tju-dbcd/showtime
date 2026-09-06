using System;
using System.Collections.Generic;
using System.Linq;
using ShowtimeBackend.Entities.ShowSession;

namespace ShowtimeBackend.Services.ShowSession;

public static class PricingChange
{
    /// <summary>
    /// 按基础票价与启用的动态调价规则，计算某评估时刻最终生效的实时票价。
    /// </summary>
    /// <remarks>
    /// 本方法仅计算、不落库；evaluationTime 决定命中哪个调价时间窗口，因此展示与结算必须统一口径：
    /// 展示报价传当前 UTC 时间；下单/改签结算必须传“座位锁创建时间 seatLock.CreateTime”，
    /// 确保成交价以锁定时点锁定、不受后续临近开演的动态调价影响。
    /// </remarks>
    /// <param name="basePrice">区域基础票价</param>
    /// <param name="sessionStartTime">场次开始时间</param>
    /// <param name="evaluationTime">计价评估时刻。展示口径 = 当前 UTC；下单/改签口径 = 座位锁创建时间 seatLock.CreateTime</param>
    /// <param name="seatSectionId">目标看台区域 ID（null 表示全场通用）</param>
    /// <param name="rules">该场次启用的动态调价规则集</param>
    public static decimal CalculateRealtimePrice(
        decimal basePrice,
        DateTime sessionStartTime,
        DateTime evaluationTime,
        long seatSectionId,
        IEnumerable<DynamicPricingRule> rules)
    {
        var matchedRule = rules
            .Where(r => r.Status == "ENABLED")
            .Where(r => r.SeatSectionId == null || r.SeatSectionId == seatSectionId)
            .Where(r => IsRuleTriggered(r, sessionStartTime, evaluationTime))
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

    public static bool IsRuleTriggered(DynamicPricingRule rule, DateTime sessionStartTime, DateTime evaluationTime)
    {
        if (rule.Status != "ENABLED") return false;

        return rule.TriggerType switch
        {
            "TIME_WINDOW" => MatchesTimeWindow(rule, (int)(sessionStartTime - evaluationTime).TotalMinutes),
            "INVENTORY_RATE" => false, // [NotImplemented] 暂未接入实时库存计算，安全返回 false，绝不误触发
            _ => false
        };
    }

    private static bool MatchesTimeWindow(DynamicPricingRule rule, int minutesToStart)
    {
        bool startMatch = !rule.StartOffsetMinutes.HasValue || minutesToStart <= rule.StartOffsetMinutes.Value;
        bool endMatch = !rule.EndOffsetMinutes.HasValue || minutesToStart >= rule.EndOffsetMinutes.Value;

        return startMatch && endMatch;
    }
}
