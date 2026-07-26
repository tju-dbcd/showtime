using ShowtimeBackend.Entities.Base;

namespace ShowtimeBackend.Entities.SeatZone;

public class SeatSection : AuditableEntity
{
    public long SeatSectionId { get; set; }
    public long SeatMapId { get; set; }
    public string SectionCode { get; set; } = null!;
    public string SectionName { get; set; } = null!;
    public string SectionType { get; set; } = "NORMAL";
    public string? SectionColor { get; set; }
    public string? FloorNo { get; set; }
    public bool IsSellable { get; set; } = true;
    public int DisplayOrder { get; set; }
    public string? Remark { get; set; }
    public SeatMap SeatMap { get; set; } = null!;
    public ICollection<Seat> Seats { get; set; } = [];
    public ICollection<SeatRuleScope> RuleScopes { get; set; } = [];
}
