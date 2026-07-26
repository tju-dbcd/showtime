using System;
using ShowtimeBackend.Entities.Base;

namespace ShowtimeBackend.Entities.ShowSessions
{
    /// <summary>
    /// 营销内容实体
    /// </summary>
    public class MarketingContent : AuditableEntity
    {
        /// <summary>
        /// 内容主键 CONTENT_ID NUMBER(19,0)
        /// </summary>
        public long ContentId { get; set; }

        /// <summary>
        /// 演出 ID 外键 SHOW_ID NUMBER(19,0)
        /// </summary>
        public long ShowId { get; set; }

        /// <summary>
        /// 内容类型 CONTENT_TYPE VARCHAR2(20 CHAR) 默认 NOTICE
        /// 允许值: NOTICE (公告), AD (广告), PROMOTION (促销)
        /// </summary>
        public string ContentType { get; set; } = "NOTICE";

        /// <summary>
        /// 标题 TITLE VARCHAR2(200 CHAR)
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// 正文内容 (CLOB 大文本)
        /// </summary>
        public string? ContentText { get; set; }

        /// <summary>
        /// 图片 URL IMAGE_URL VARCHAR2(500 CHAR)
        /// </summary>
        public string? ImageUrl { get; set; }

        /// <summary>
        /// 排序号 SORT_ORDER NUMBER(5,0) 默认 0
        /// </summary>
        public int SortOrder { get; set; } = 0;

        /// <summary>
        /// 状态 STATUS VARCHAR2(20 CHAR) 默认 ENABLED
        /// 允许值: ENABLED (启用), DISABLED (禁用)
        /// </summary>
        public string Status { get; set; } = "ENABLED";

        /// <summary>
        /// 发布时间 PUBLISH_TIME TIMESTAMP(6) (可空)
        /// </summary>
        public DateTime? PublishTime { get; set; }

        // =============== 导航属性 =========
        /// <summary>
        /// 关联的演出实体
        /// </summary>
        public virtual Show Show { get; set; } = null!;
    }
}
