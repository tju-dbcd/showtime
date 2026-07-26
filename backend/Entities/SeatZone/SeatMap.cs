using ShowtimeBackend.Entities.Base;

namespace ShowtimeBackend.Entities.SeatZone;

public class SeatMap : AuditableEntity
{
    public long SeatMapId { get; set; }
    public long VenueId { get; set; }
    public string MapCode { get; set; } = null!;
    public string MapName { get; set; } = null!;
    public string MapVersion { get; set; } = "V1";
    public bool IsDefault { get; set; }
    public decimal? MapWidth { get; set; }
    public decimal? MapHeight { get; set; }
    public string MapStatus { get; set; } = "DRAFT";
    public string? Remark { get; set; }
    public ICollection<SeatSection> Sections { get; set; } = [];
    public ICollection<SeatRuleScope> RuleScopes { get; set; } = [];
}
