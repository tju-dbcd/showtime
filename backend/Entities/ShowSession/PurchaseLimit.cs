namespace ShowtimeBackend.Entities.Session // 由base派生出session类管理演出场次相关数据

using ShowtimeBackend.Entities.Base;

namespace PurchaseLimit {
    /// <summary>
    /// 演出购买限制实体
    /// </summary>
    public class PurchaseLimit : AuditableEntity
    {
        {
        /// <summary>
        /// 限购策略唯一标识，主键，自增
        /// </summary>
        public long LimitId { get; set; }

        /// <summary>
        /// 策略名称（如“每用户限购4张”）
        /// </summary>
        public string LimitName { get; set; } = string.Empty;

        /// <summary>
        /// 关联演出 ID（外键），null 表示不限演出
        /// </summary>
        public long? ShowId { get; set; }

        /// <summary>
        /// 关联场次 ID（外键），null 表示针对整个演出
        /// </summary>
        public long? SessionId { get; set; }

        /// <summary>
        /// 限制渠道：WEB/APP/MINI_PROGRAM，null 表示全渠道
        /// </summary>
        public string? Channel { get; set; }

        /// <summary>
        /// 用户类型：NORMAL/MEMBER/VIP，null 表示所有用户
        /// </summary>
        public string? UserType { get; set; }

        /// <summary>
        /// 最多购买数量（按 LIMIT_TYPE 解释）
        /// </summary>
        public int MaxBuyCount { get; set; } = 1;

        /// <summary>
        /// 限制类型：TICKET-按票数，ORDER-按订单数
        /// </summary>
        public string LimitType { get; set; } = "TICKET";

        /// <summary>
        /// 限购生效时间，null 表示永久有效
        /// </summary>
        public DateTime? StartTime { get; set; }

        /// <summary>
        /// 限购失效时间
        /// </summary>
        public DateTime? EndTime { get; set; }

        /// <summary>
        /// 状态：ENABLED-启用，DISABLED-停用
        /// </summary>
        public string Status { get; set; } = "ENABLED";

        #region 外键导航属性 (Navigation Properties)

        /// <summary>
        /// 关联演出导航属性
        /// </summary>
        public virtual Show? Show { get; set; }

        /// <summary>
        /// 关联场次导航属性
        /// </summary>
        public virtual Session? Session { get; set; }

        #endregion
    }
}

