using ShowtimeBackend.Entities.Base;

namespace ShowtimeBackend.Entities.SeatZone;

public class SeatRule : AuditableEntity
{
    public long SeatRuleId { get; set; }
    public string RuleCode { get; set; } = null!;
    public string RuleName { get; set; } = null!;

    /// <summary>
    /// 选座规则类型：CONTINUOUS-连座，NO_SINGLE_LEFT-避免余座，LIMIT_COUNT-数量限制，SECTION_LIMIT-票区限制。
    /// </summary>
    public string RuleType { get; set; } = null!;

    /// <summary>
    /// 规则允许的最小选座数量。
    /// </summary>
    public int MinSeatCount { get; set; } = 1;

    /// <summary>
    /// 规则允许的最大选座数量。
    /// </summary>
    public int MaxSeatCount { get; set; } = 10;

    /// <summary>
    /// 是否允许一次选座跨越不同排。
    /// </summary>
    public bool AllowCrossRow { get; set; }

    /// <summary>
    /// 是否允许一次选座跨越不同票区。
    /// </summary>
    public bool AllowCrossSection { get; set; }

    /// <summary>
    /// 多条规则同时命中时的处理优先级，数值越小越优先。
    /// </summary>
    public int Priority { get; set; } = 100;

    /// <summary>
    /// 规则状态：ENABLED-启用，DISABLED-停用。
    /// </summary>
    public string RuleStatus { get; set; } = "ENABLED";
    public string? Remark { get; set; }
    public ICollection<SeatRuleScope> Scopes { get; set; } = [];
}
