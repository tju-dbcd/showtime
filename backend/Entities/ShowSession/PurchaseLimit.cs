using System;
using ShowtimeBackend.Entities.Base;

namespace ShowtimeBackend.Entities.ShowSession
{
    /// <summary>
    /// 限购规则实体
    /// </summary>
    public class PurchaseLimit : AuditableEntity
    {
        /// <summary>
        /// 限购规则主键 LIMIT_ID NUMBER(19,0)
        /// </summary>
        public long LimitId { get; set; }

        /// <summary>
        /// 限购规则名称 LIMIT_NAME VARCHAR2(100 CHAR)
        /// </summary>
        public string LimitName { get; set; } = string.Empty;

        /// <summary>
        /// 演出 ID 外键 SHOW_ID NUMBER(19,0) (可空)
        /// </summary>
        public long? ShowId { get; set; }

        /// <summary>
        /// 场次 ID 外键 SESSION_ID NUMBER(19,0) (可空)
        /// </summary>
        public long? SessionId { get; set; }

        /// <summary>
        /// 渠道 CHANNEL VARCHAR2(20 CHAR) (可空)
        /// 允许值: WEB, APP, MINI_PROGRAM
        /// </summary>
        public string? Channel { get; set; }

        /// <summary>
        /// 用户类型 USER_TYPE VARCHAR2(20 CHAR) (可空)
        /// 允许值: NORMAL, MEMBER, VIP
        /// </summary>
        public string? UserType { get; set; }

        /// <summary>
        /// 最大购买数量 MAX_BUY_COUNT NUMBER(5,0)
        /// </summary>
        public int MaxBuyCount { get; set; }

        /// <summary>
        /// 限购类型 LIMIT_TYPE VARCHAR2(20 CHAR) 默认 TICKET
        /// 允许值: TICKET (按票张数限购), ORDER (按订单笔数限购)
        /// </summary>
        public string LimitType { get; set; } = "TICKET";

        /// <summary>
        /// 限购生效开始时间 START_TIME TIMESTAMP(6) (可空)
        /// </summary>
        public DateTime? StartTime { get; set; }

        /// <summary>
        /// 限购生效结束时间 END_TIME TIMESTAMP(6) (可空)
        /// </summary>
        public DateTime? EndTime { get; set; }

        /// <summary>
        /// 状态 STATUS VARCHAR2(20 CHAR) 默认 ENABLED
        /// 允许值: ENABLED (启用), DISABLED (禁用)
        /// </summary>
        public string Status { get; set; } = "ENABLED";

        // ================= 导航属性 =================
        /// <summary>
        /// 关联的演出实体 (可空)
        /// </summary>
        public virtual Show? Show { get; set; }

        /// <summary>
        /// 关联的场次实体 (可空)
        /// </summary>
        public virtual ShowSession? ShowSession { get; set; }
    }
}
