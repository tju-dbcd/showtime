namespace ShowtimeBackend.Entities.ShowSessions
{
    /// <summary>
    /// 演出标签关联实体（中间表）
    /// </summary>
    public class ShowTag
    {
        /// <summary>
        /// 主键 SHOW_TAG_ID NUMBER(19,0)
        /// </summary>
        public long ShowTagId { get; set; }

        /// <summary>
        /// 演出 ID 外键 SHOW_ID NUMBER(19,0)
        /// </summary>
        public long ShowId { get; set; }

        /// <summary>
        /// 标签 ID 外键 TAG_ID NUMBER(19,0)
        /// </summary>
        public long TagId { get; set; }

        // ============= 导航属性 ================
        /// <summary>
        /// 关联的演出实体
        /// </summary>
        public virtual Show Show { get; set; } = null!;

        /// <summary>
        /// 关联的标签实体
        /// </summary>
        public virtual Tag Tag { get; set; } = null!;
    }
}
