using ShowtimeBackend.Entities.Base;

namespace ShowtimeBackend.Entities.ShowSession
{
    /// <summary>
    /// 场馆实体
    /// </summary>
    public class Venue : AuditableEntity
    {
        /// <summary>
        /// 主键 VENUE_ID NUMBER(19,0)
        /// </summary>
        public long VenueId { get; set; }

        /// <summary>
        /// 场馆名称 VENUE_NAME VARCHAR2(100 CHAR)
        /// </summary>
        public string VenueName { get; set; } = string.Empty;

        /// <summary>
        /// 地址 ADDRESS VARCHAR2(200 CHAR)
        /// </summary>
        public string? Address { get; set; }

        /// <summary>
        /// 联系电话 CONTACT_PHONE VARCHAR2(20 CHAR)
        /// </summary>
        public string? ContactPhone { get; set; }

        /// <summary>
        /// 状态 STATUS VARCHAR2(20 CHAR) 默认 ENABLED
        /// 允许值: ENABLED, DISABLED
        /// </summary>
        public string Status { get; set; } = "ENABLED";

        /// <summary>
        /// 备注 REMARK VARCHAR2(255 CHAR)
        /// </summary>
        public string? Remark { get; set; }
    }
}
