using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ShowtimeBackend.Common;
using ShowtimeBackend.Controllers.ShowSession.Admin;
using ShowtimeBackend.Controllers.ShowSession.Client;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.ShowSessionChange;
using ShowtimeBackend.DTOs.ShowSessionDto;
using ShowtimeBackend.Entities.ShowSession;
using ShowtimeBackend.Services.Impl;
using ShowtimeBackend.Services.ShowSession;

public class PricingChangeTests
{
    private readonly DateTime _sessionStart = new(2026, 9, 1, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void IsRuleTriggered_InventoryRate_AlwaysReturnsFalse()
    {
        var rule = new DynamicPricingRule
        {
            Status = "ENABLED",
            TriggerType = "INVENTORY_RATE"
        };

        var isTriggered = PricingChange.IsRuleTriggered(rule, _sessionStart, _sessionStart.AddHours(-1));

        Assert.False(isTriggered);
    }

    [Theory]
    [InlineData(180, 60, 60, true)]   // 开演前 120 分钟（处于 60~180 窗口内）
    [InlineData(180, 60, 200, false)] // 开演前 200 分钟（未到触发起点）
    [InlineData(180, 60, 30, false)]  // 开演前 30 分钟（已过触发终点）
    public void IsRuleTriggered_TimeWindow_EvaluatesOffsetCorrectly(
        int startOffset, int endOffset, int minutesBeforeStart, bool expectedTriggered)
    {
        var rule = new DynamicPricingRule
        {
            Status = "ENABLED",
            TriggerType = "TIME_WINDOW",
            StartOffsetMinutes = startOffset,
            EndOffsetMinutes = endOffset
        };

        var nowUtc = _sessionStart.AddMinutes(-minutesBeforeStart);
        var isTriggered = PricingChange.IsRuleTriggered(rule, _sessionStart, nowUtc);

        Assert.Equal(expectedTriggered, isTriggered);
    }

    [Fact]
    public void CalculateRealtimePrice_AppliesHighestPriorityMatchedRule()
    {
        var nowUtc = _sessionStart.AddMinutes(-100);
        var rules = new List<DynamicPricingRule>
        {
            new() { Priority = 10, TriggerType = "TIME_WINDOW", StartOffsetMinutes = 120, EndOffsetMinutes = 60, AdjustmentType = "DISCOUNT_RATE", AdjustmentValue = 0.8m, Status = "ENABLED" },
            new() { Priority = 20, TriggerType = "TIME_WINDOW", StartOffsetMinutes = 120, EndOffsetMinutes = 60, AdjustmentType = "FIXED_PRICE", AdjustmentValue = 150m, Status = "ENABLED" }
        };

        var result = PricingChange.CalculateRealtimePrice(200m, _sessionStart, nowUtc, seatSectionId: 1, rules);

        Assert.Equal(150m, result); // 优先应用 Priority=20
    }

    [Fact]
    public void CalculateRealtimePrice_FiltersBySeatSectionId()
    {
        var nowUtc = _sessionStart.AddMinutes(-100);
        var rules = new List<DynamicPricingRule>
        {
            new() { SeatSectionId = 99, Priority = 10, TriggerType = "TIME_WINDOW", StartOffsetMinutes = 120, EndOffsetMinutes = 60, AdjustmentType = "FIXED_PRICE", AdjustmentValue = 50m, Status = "ENABLED" },
            new() { SeatSectionId = null, Priority = 5, TriggerType = "TIME_WINDOW", StartOffsetMinutes = 120, EndOffsetMinutes = 60, AdjustmentType = "DISCOUNT_RATE", AdjustmentValue = 0.9m, Status = "ENABLED" }
        };

        // 传入 seatSectionId = 1，排除 SeatSectionId = 99，命中通用规则（SeatSectionId = null）
        var result = PricingChange.CalculateRealtimePrice(100m, _sessionStart, nowUtc, seatSectionId: 1, rules);

        Assert.Equal(90m, result);
    }

    [Theory]
    [InlineData("DISCOUNT_RATE", 0.85, 100, 85)]
    [InlineData("AMOUNT_OFF", 30, 100, 70)]
    [InlineData("AMOUNT_OFF", 150, 100, 0)] // 防扣减为负数
    [InlineData("FIXED_PRICE", 250, 100, 250)]
    public void CalculateRealtimePrice_AdjustmentTypes_CalculatesCorrectly(
        string adjustmentType, decimal value, decimal basePrice, decimal expectedPrice)
    {
        var nowUtc = _sessionStart.AddMinutes(-30);
        var rules = new List<DynamicPricingRule>
        {
            new()
            {
                Status = "ENABLED",
                TriggerType = "TIME_WINDOW",
                StartOffsetMinutes = 60,
                EndOffsetMinutes = 0,
                AdjustmentType = adjustmentType,
                AdjustmentValue = value
            }
        };

        var price = PricingChange.CalculateRealtimePrice(basePrice, _sessionStart, nowUtc, seatSectionId: 1, rules);

        Assert.Equal(expectedPrice, price);
    }
}
