using System;
using ShowtimeBackend.Entities.Base;

namespace ShowtimeBackend.Entities.ShowSessions
{
    /// <summary>
    /// 演出主体实体
    /// </summary>
    public class Show : AuditableEntity
    {
        /// <summary>
        /// 主键 SHOW_ID NUMBER(19,0)
        /// </summary>
        public long ShowId { get; set; }

        /// <summary>
        /// 演出名称 SHOW_NAME VARCHAR2(200 CHAR)
        /// </summary>
        public string ShowName { get; set; } = string.Empty;

        /// <summary>
        /// 分类 ID 外键 CATEGORY_ID NUMBER(19,0)
        /// </summary>
        public long CategoryId { get; set; }

        /// <summary>
        /// 描述 DESCRIPTION VARCHAR2(2000 CHAR)
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// 时长(分钟) DURATION_MINUTES NUMBER(5,0)
        /// </summary>
        public int? DurationMinutes { get; set; }

        /// <summary>
        /// 海报地址 POSTER_URL VARCHAR2(500 CHAR)
        /// </summary>
        public string? PosterUrl { get; set; }

        /// <summary>
        /// 状态 STATUS VARCHAR2(20 CHAR) 默认 DRAFT
        /// 允许值: DRAFT, PUBLISHED, UNPUBLISHED
        /// </summary>
        public string Status { get; set; } = "DRAFT";

        /// <summary>
        /// 审核状态 AUDIT_STATUS VARCHAR2(20 CHAR) 默认 PENDING
        /// 允许值: PENDING, APPROVED, REJECTED
        /// </summary>
        public string AuditStatus { get; set; } = "PENDING";

        /// <summary>
        /// 审核人 AUDIT_BY VARCHAR2(50 CHAR)
        /// </summary>
        public string? AuditBy { get; set; }

        /// <summary>
        /// 审核时间 AUDIT_TIME TIMESTAMP(6)
        /// </summary>
        public DateTime? AuditTime { get; set; }

        // ================= 导航属性 =================
        /// <summary>
        /// 分类导航属性
        /// </summary>
        public virtual Category Category { get; set; } = null!;

        public virtual ICollection<ShowTag> ShowTags { get; set; } = new List<ShowTag>();
    }
}
