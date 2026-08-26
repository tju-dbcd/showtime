using System;
using ShowtimeBackend.Entities.Base;
using System.ComponentModel.DataAnnotations;

namespace ShowtimeBackend.Entities.ShowSession;

/// <summary>
/// 动态调价规则实体
/// </summary>
public class DynamicPricingRule : AuditableEntity
{
    [Key]
    public long DynamicPricingRuleId { get; set; }
    public long SessionId { get; set; }

    /// <summary>
    /// 作用的看台 ID
    /// </summary>
    public long? SeatSectionId { get; set; }

    public string RuleName { get; set; } = string.Empty;

    public string TriggerType { get; set; } = "TIME_WINDOW";

    /// <summary>
    /// 相对 StartTime 的触发起点/终点分钟数
    /// </summary>
    public int? StartOffsetMinutes { get; set; }
    public int? EndOffsetMinutes { get; set; }

    /// <summary>
    /// 调价模式
    /// </summary>
    public string AdjustmentType { get; set; } = "DISCOUNT_RATE";

    /// <summary>
    /// 调价数值 (折扣系数或扣减金额)
    /// </summary>
    public decimal AdjustmentValue { get; set; }

    /// <summary>
    /// 优先级越高越先
    /// </summary>
    public int Priority { get; set; } = 0;

    public string Status { get; set; } = "ENABLED";

    // 导航属性
    public virtual ShowSession ShowSession { get; set; } = null!;
}
