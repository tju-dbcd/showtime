using ShowtimeBackend.Entities.Base;

namespace ShowtimeBackend.Entities.SeatZone;

public class Seat : AuditableEntity
{
    public long SeatId { get; set; }
    public long SeatSectionId { get; set; }
    public string RowCode { get; set; } = null!;
    public string SeatNo { get; set; } = null!;

    /// <summary>
    /// 座位在票区中的行坐标。
    /// </summary>
    public int RowIndex { get; set; }

    /// <summary>
    /// 座位在票区中的列坐标。
    /// </summary>
    public int ColIndex { get; set; }

    /// <summary>
    /// 座位在座位图编辑画布中的横坐标。
    /// </summary>
    public decimal XCoord { get; set; }

    /// <summary>
    /// 座位在座位图编辑画布中的纵坐标。
    /// </summary>
    public decimal YCoord { get; set; }

    /// <summary>
    /// 座位类型：NORMAL-普通座，COUPLE-情侣座，ACCESSIBLE-无障碍座，COMPANION-陪同座。
    /// </summary>
    public string SeatType { get; set; } = "NORMAL";

    /// <summary>
    /// 座位状态：ENABLED-可用，DISABLED-停用，MAINTENANCE-维修中。
    /// </summary>
    public string SeatStatus { get; set; } = "ENABLED";

    /// <summary>
    /// 是否位于过道一侧。
    /// </summary>
    public bool IsAisleSide { get; set; }

    /// <summary>
    /// 是否允许该座位参与售卖。
    /// </summary>
    public bool IsSellable { get; set; } = true;
    public string? Remark { get; set; }
    public SeatSection SeatSection { get; set; } = null!;
}
