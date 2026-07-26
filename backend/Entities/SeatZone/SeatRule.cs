using ShowtimeBackend.Entities.Base;

namespace ShowtimeBackend.Entities.SeatZone;

public class SeatRule : AuditableEntity
{
    public long SeatRuleId { get; set; }
    public string RuleCode { get; set; } = null!;
    public string RuleName { get; set; } = null!;
    public string RuleType { get; set; } = null!;
    public int MinSeatCount { get; set; } = 1;
    public int MaxSeatCount { get; set; } = 10;
    public bool AllowCrossRow { get; set; }
    public bool AllowCrossSection { get; set; }
    public int Priority { get; set; } = 100;
    public string RuleStatus { get; set; } = "ENABLED";
    public string? Remark { get; set; }
    public ICollection<SeatRuleScope> Scopes { get; set; } = [];
}
