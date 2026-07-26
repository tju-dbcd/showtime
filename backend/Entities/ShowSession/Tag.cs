using ShowtimeBackend.Entities.Base;

namespace ShowtimeBackend.Entities.ShowSessions
{
    /// <summary>
    /// 标签实体
    /// </summary>
    public class Tag : AuditableEntity
    {
        /// <summary>
        /// 主键 TAG_ID NUMBER(19,0)
        /// </summary>
        public long TagId { get; set; }

        /// <summary>
        /// 标签名称 TAG_NAME VARCHAR2(50 CHAR)
        /// </summary>
        public string TagName { get; set; } = string.Empty;

        /// <summary>
        /// 颜色 COLOR VARCHAR2(20 CHAR)
        /// </summary>
        public string? Color { get; set; }

        /// <summary>
        /// 状态 STATUS NUMBER(1,0) 默认 1
        /// 允许值: 0 (禁用/停用), 1 (启用/正常)
        /// </summary>
        public int Status { get; set; } = 1;

        public virtual ICollection<ShowTag> ShowTags { get; set; } = new List<ShowTag>();
    }
}
