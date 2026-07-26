//namespace ShowtimeBackend.Entities.Session // 由base派生出session类管理演出场次相关数据

using ShowtimeBackend.Entities.Base;

namespace ShowtimeBackend.Entities.ShowSessions
{

    public class MarketingContent : AuditableEntity
    {
        /// <summary>
        /// 营销内容唯一标识，主键，自增
        /// </summary>
        public long ContentId { get; set; }

        /// <summary>
        /// 关联演出 ID（外键）
        /// </summary>
        public long ShowId { get; set; }

        /// <summary>
        /// 内容类型：NOTICE-公告，AD-广告，PROMOTION-促销活动
        /// </summary>
        public string ContentType { get; set; } = "NOTICE";

        /// <summary>
        /// 内容标题
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// 内容正文（支持长文本 CLOB）
        /// </summary>
        public string? ContentText { get; set; }

        /// <summary>
        /// 图片地址
        /// </summary>
        public string? ImageUrl { get; set; }

        /// <summary>
        /// 显示顺序
        /// </summary>
        public int SortOrder { get; set; } = 0;

        /// <summary>
        /// 状态：ENABLED-启用，DISABLED-停用
        /// </summary>
        public string Status { get; set; } = "ENABLED";

        /// <summary>
        /// 发布时间
        /// </summary>
        public DateTime? PublishTime { get; set; }

        #region 外键导航属性 (Navigation Properties)

        /// <summary>
        /// 关联演出导航属性
        /// </summary>
        public virtual Show? Show { get; set; }

        #endregion
    }
}
