using System.ComponentModel.DataAnnotations;
namespace ShowtimeBackend.DTOs.ShowSessionChange;//修改票价策略

//输入校验规范
public record CreateDynamicPricingRuleRequest(
    long? SeatSectionId,

    [Required(ErrorMessage = "规则名称不能为空")]
    [StringLength(100, ErrorMessage = "规则名称不能超过100字符")]
    string RuleName,

    /// <summary>
    /// 触发类型：TIME_WINDOW 或 INVENTORY_RATE
    /// </summary>
    /// <remarks> INVENTORY_RATE 当前评估计算逻辑恒定返回 false。</remarks>
    [Required]
    [RegularExpression("^(TIME_WINDOW|INVENTORY_RATE)$", ErrorMessage = "TriggerType 必须为 TIME_WINDOW 或 INVENTORY_RATE")]
    string TriggerType,

    /// <summary>
    /// 相对场次开始时间倒计时的触发窗口“较早边界”（分钟）。语义：从开演时刻向前倒推，
    /// StartOffsetMinutes 与 EndOffsetMinutes 共同界定窗口，须满足 StartOffsetMinutes &gt;= EndOffsetMinutes。
    /// 例：开演前 120 分钟~30 分钟内触发 → StartOffsetMinutes=120、EndOffsetMinutes=30。
    /// </summary>
    int? StartOffsetMinutes,

    /// <summary>
    /// 相对场次开始时间倒计时的触发窗口“较晚边界”（分钟）。语义见 StartOffsetMinutes。
    /// </summary>
    int? EndOffsetMinutes,

    [Required]
    [RegularExpression("^(DISCOUNT_RATE|AMOUNT_OFF|FIXED_PRICE)$", ErrorMessage = "AdjustmentType 必须为 DISCOUNT_RATE、AMOUNT_OFF 或 FIXED_PRICE")]
    string AdjustmentType,

    [Range(0.00, 99999999.99, ErrorMessage = "AdjustmentValue输入错误")]
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
