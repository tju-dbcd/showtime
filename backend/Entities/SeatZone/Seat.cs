using ShowtimeBackend.Entities.Base;

namespace ShowtimeBackend.Entities.SeatZone;

public class Seat : AuditableEntity
{
    public long SeatId { get; set; }
    public long SeatSectionId { get; set; }
    public string RowCode { get; set; } = null!;
    public string SeatNo { get; set; } = null!;
    public int RowIndex { get; set; }
    public int ColIndex { get; set; }
    public decimal XCoord { get; set; }
    public decimal YCoord { get; set; }
    public string SeatType { get; set; } = "NORMAL";
    public string SeatStatus { get; set; } = "ENABLED";
    public bool IsAisleSide { get; set; }
    public bool IsSellable { get; set; } = true;
    public string? Remark { get; set; }
    public SeatSection SeatSection { get; set; } = null!;
}
