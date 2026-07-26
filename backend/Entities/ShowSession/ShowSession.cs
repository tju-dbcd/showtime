using System;
using ShowtimeBackend.Entities.Base;

namespace ShowtimeBackend.Entities.ShowSessions
{
    /// <summary>
    /// 演出场次实体
    /// </summary>
    public class ShowSession : AuditableEntity
    {
        /// <summary>
        /// 场次主键 SESSION_ID NUMBER(19,0)
        /// </summary>
        public long SessionId { get; set; }

        /// <summary>
        /// 演出 ID 外键 SHOW_ID NUMBER(19,0)
        /// </summary>
        public long ShowId { get; set; }

        /// <summary>
        /// 座位图 ID 外键 SEAT_MAP_ID NUMBER(19,0)
        /// </summary>
        public long SeatMapId { get; set; }

        /// <summary>
        /// 演出开始时间 START_TIME TIMESTAMP(6)
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// 演出结束时间 END_TIME TIMESTAMP(6)
        /// </summary>
        public DateTime EndTime { get; set; }

        /// <summary>
        /// 售票开始时间 SALE_START_TIME TIMESTAMP(6)
        /// </summary>
        public DateTime SaleStartTime { get; set; }

        /// <summary>
        /// 售票结束时间 SALE_END_TIME TIMESTAMP(6)
        /// </summary>
        public DateTime SaleEndTime { get; set; }

        /// <summary>
        /// 场次状态 SESSION_STATUS VARCHAR2(20 CHAR) 默认 UPCOMING
        /// 允许值: UPCOMING, PRESALE, ONSALE, SOLD_OUT, ENDED
        /// </summary>
        public string SessionStatus { get; set; } = "UPCOMING";

        // ================= 导航属性 ===============
        /// <summary>
        /// 关联的演出实体
        /// </summary>
        public virtual Show Show { get; set; } = null!;

        /// <summary>
        /// TODO:关联的座位图实体等待关联
        /// </summary>
        //public virtual SeatMap SeatMap { get; set; } = null!;
    }
}
