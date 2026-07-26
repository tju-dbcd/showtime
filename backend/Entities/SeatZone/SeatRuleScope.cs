using ShowtimeBackend.Entities.Base;

namespace ShowtimeBackend.Entities.SeatZone;

public class SeatRuleScope : AuditableEntity
{
    public long RuleScopeId { get; set; }
    public long SeatRuleId { get; set; }

    /// <summary>
    /// 规则生效范围：MAP-整张座位图，SECTION-指定票区。
    /// </summary>
    public string ScopeType { get; set; } = null!;

    /// <summary>
    /// 生效范围对应的座位图；ScopeType 为 MAP 时填写。
    /// </summary>
    public long? SeatMapId { get; set; }

    /// <summary>
    /// 生效范围对应的票区；ScopeType 为 SECTION 时填写。
    /// </summary>
    public long? SeatSectionId { get; set; }

    /// <summary>
    /// 范围状态：ENABLED-启用，DISABLED-停用。
    /// </summary>
    public string ScopeStatus { get; set; } = "ENABLED";
    public SeatRule SeatRule { get; set; } = null!;
    public SeatMap? SeatMap { get; set; }
    public SeatSection? SeatSection { get; set; }
}
