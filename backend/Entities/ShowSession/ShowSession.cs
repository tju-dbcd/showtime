//namespace ShowtimeBackend.Entities.Session // 由base派生出session类管理演出场次相关数据

using ShowtimeBackend.Entities.Base;

namespace ShowtimeBackend.Entities{
    /// <summary>
    /// 演出场次实体
    /// </summary>
    public class ShowSession : AuditableEntity
    {
        /// <summary>
        /// 场次唯一标识，主键，自增
        /// </summary>
        public long SessionId { get; set; }

        /// <summary>
        /// 所属演出 ID（外键）
        /// </summary>
        public long ShowId { get; set; }

        /// <summary>
        /// 座位图/票区配置 ID（外键）
        /// </summary>
        public long SeatMapId { get; set; }

        /// <summary>
        /// 演出开始时间
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// 演出结束时间
        /// </summary>
        public DateTime EndTime { get; set; }

        /// <summary>
        /// 售票开始时间
        /// </summary>
        public DateTime SaleStartTime { get; set; }

        /// <summary>
        /// 售票结束时间
        /// </summary>
        public DateTime SaleEndTime { get; set; }

        /// <summary>
        /// 状态：UPCOMING-待售，PRESALE-预售，ONSALE-在售，SOLD_OUT-售罄，ENDED-已结束
        /// </summary>
        public string SessionStatus { get; set; } = "UPCOMING";

        #region 导航属性 (Navigation Properties)

        /// <summary>
        /// 所属演出导航属性（多对一）
        /// </summary>
        public virtual Show Show { get; set; } = null!;

        // 如果后续有 SeatMap 实体，取消下方注释：
        // public virtual SeatMap SeatMap { get; set; } = null!;

        #endregion
    }
}
