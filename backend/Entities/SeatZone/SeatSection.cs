using ShowtimeBackend.Entities.Base;
using ShowtimeBackend.Entities.ShowSession;

namespace ShowtimeBackend.Entities.SeatZone;

public class SeatSection : AuditableEntity
{
    public long SeatSectionId { get; set; }
    public long SeatMapId { get; set; }
    public string SectionCode { get; set; } = null!;
    public string SectionName { get; set; } = null!;

    /// <summary>
    /// 票区类型：NORMAL-普通区，VIP-VIP 区，ACCESSIBLE-无障碍区，STANDING-站票区。
    /// </summary>
    public string SectionType { get; set; } = "NORMAL";

    /// <summary>
    /// 前端展示票区时使用的颜色值。
    /// </summary>
    public string? SectionColor { get; set; }

    /// <summary>
    /// 票区所在楼层，可为空。
    /// </summary>
    public string? FloorNo { get; set; }

    /// <summary>
    /// 是否允许在该票区内售票。
    /// </summary>
    public bool IsSellable { get; set; } = true;

    /// <summary>
    /// 同一座位图内的展示顺序。
    /// </summary>
    public int DisplayOrder { get; set; }
    public string? Remark { get; set; }
    public SeatMap SeatMap { get; set; } = null!;
    public ICollection<Seat> Seats { get; set; } = [];
    public ICollection<SeatRuleScope> RuleScopes { get; set; } = [];

    // 导航属性
    public ICollection<ShowtimeBackend.Entities.ShowSession.PriceStrategy> PriceStrategies { get; set; } = [];
}
