using ShowtimeBackend.Entities.Base;

namespace ShowtimeBackend.Entities.SeatZone;

public class SeatRuleScope : AuditableEntity
{
    public long RuleScopeId { get; set; }
    public long SeatRuleId { get; set; }
    public string ScopeType { get; set; } = null!;
    public long? SeatMapId { get; set; }
    public long? SeatSectionId { get; set; }
    public string ScopeStatus { get; set; } = "ENABLED";
    public SeatRule SeatRule { get; set; } = null!;
    public SeatMap? SeatMap { get; set; }
    public SeatSection? SeatSection { get; set; }
}
