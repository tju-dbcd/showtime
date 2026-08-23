namespace ShowtimeBackend.DTOs.ShowSessionChange;//修改票价策略

public record CreateDynamicPricingRuleRequest(
    long? SeatSectionId,
    string RuleName,
    string TriggerType, // TIME_WINDOW / INVENTORY_RATE
    int? StartOffsetMinutes,
    int? EndOffsetMinutes,
    string AdjustmentType, // DISCOUNT_RATE / AMOUNT_OFF / FIXED_PRICE
    decimal AdjustmentValue,
    int Priority = 0
);

public record DynamicPricingRuleDto(
    long RuleId,
    long SessionId,
    long? SeatSectionId,
    string RuleName,
    string TriggerType,
    int? StartOffsetMinutes,
    int? EndOffsetMinutes,
    string AdjustmentType,
    decimal AdjustmentValue,
    int Priority,
    string Status
);
