using ShowtimeBackend.Entities.Base;

namespace ShowtimeBackend.Entities.SeatZone;

public class SeatMap : AuditableEntity
{
    public long SeatMapId { get; set; }
    public long VenueId { get; set; }
    public string MapCode { get; set; } = null!;
    public string MapName { get; set; } = null!;

    /// <summary>
    /// 座位图版本号，用于区分同一场馆布局的不同迭代。
    /// </summary>
    public string MapVersion { get; set; } = "V1";

    /// <summary>
    /// 是否为该场馆当前默认使用的座位图。
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// 座位图编辑画布宽度，可为空。
    /// </summary>
    public decimal? MapWidth { get; set; }

    /// <summary>
    /// 座位图编辑画布高度，可为空。
    /// </summary>
    public decimal? MapHeight { get; set; }

    /// <summary>
    /// 座位图状态：DRAFT-草稿，ENABLED-启用，DISABLED-停用。
    /// </summary>
    public string MapStatus { get; set; } = "DRAFT";
    public string? Remark { get; set; }
    public ICollection<SeatSection> Sections { get; set; } = [];
    public ICollection<SeatRuleScope> RuleScopes { get; set; } = [];
}
