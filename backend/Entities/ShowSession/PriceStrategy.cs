using System;
using ShowtimeBackend.Entities.Base;

namespace ShowtimeBackend.Entities.ShowSessions
{
    /// <summary>
    /// 票价策略实体
    /// </summary>
    public class PriceStrategy : AuditableEntity
    {
        /// <summary>
        /// 票价策略主键 PRICE_STRATEGY_ID NUMBER(19,0)
        /// </summary>
        public long PriceStrategyId { get; set; }

        /// <summary>
        /// 场次 ID 外键 SESSION_ID NUMBER(19,0)
        /// </summary>
        public long SessionId { get; set; }

        /// <summary>
        /// 座位区域 ID 外键 SEAT_SECTION_ID NUMBER(19,0)
        /// </summary>
        public long SeatSectionId { get; set; }

        /// <summary>
        /// 策略名称 STRATEGY_NAME VARCHAR2(100 CHAR)
        /// </summary>
        public string StrategyName { get; set; } = string.Empty;

        /// <summary>
        /// 票价类型 PRICE_TYPE VARCHAR2(20 CHAR) 默认 STANDARD
        /// 允许值: EARLY_BIRD (早鸟票), PRESALE (预售票), STANDARD (标准票), VIP (VIP票), MEMBER (会员票)
        /// </summary>
        public string PriceType { get; set; } = "STANDARD";

        /// <summary>
        /// 价格 PRICE NUMBER(10,2) (>= 0)
        /// </summary>
        public decimal Price { get; set; }

        /// <summary>
        /// 售票开始时间 SALE_START_TIME TIMESTAMP(6)
        /// </summary>
        public DateTime SaleStartTime { get; set; }

        /// <summary>
        /// 售票结束时间 SALE_END_TIME TIMESTAMP(6)
        /// </summary>
        public DateTime SaleEndTime { get; set; }

        /// <summary>
        /// 优先级 PRIORITY NUMBER(5,0) 默认 0
        /// </summary>
        public int Priority { get; set; } = 0;

        /// <summary>
        /// 额度/配额 QUOTA NUMBER(10,0) (可空，为空表示不限制)
        /// </summary>
        public long? Quota { get; set; }

        /// <summary>
        /// 状态 STATUS VARCHAR2(20 CHAR) 默认 ENABLED
        /// 允许值: ENABLED (启用), DISABLED (禁用)
        /// </summary>
        public string Status { get; set; } = "ENABLED";

        // ============= 导航属性 ================
        /// <summary>
        /// 关联的场次实体
        /// </summary>
        public virtual ShowSession ShowSession { get; set; } = null!;

        /// <summary>
        /// TODO:关联的看台/区域实体待联合
        /// </summary>
        //public virtual SeatSection SeatSection { get; set; } = null!;
    }
}
