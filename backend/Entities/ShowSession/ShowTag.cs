//namespace ShowtimeBackend.Entities.Session // 由base派生出session类管理演出场次相关数据

using ShowtimeBackend.Entities.Base;

/// <summary>
/// 演出标签关联实体
/// </summary>>
/// <remarks>
/// 没有审计字段仅作为一个关联表使用不需要继承自AuditableEntity
/// </remarks>>
namespace ShowtimeBackend.Entities.ShowSessions
{
    public class ShowTag 
    {
        /// <summary>
        /// 关联记录唯一标识，主键，自增
        /// </summary>
        public long ShowTagId { get; set; }

        /// <summary>
        /// 演出 ID，外键 SHOW.SHOW_ID
        /// </summary>
        public long ShowId { get; set; }

        /// <summary>
        /// 标签 ID，外键 TAG.TAG_ID
        /// </summary>
        public long TagId { get; set; }

        /// <summary>
        /// 关联的演出实体对象
        /// </summary>
        public virtual Show Show { get; set; } = null!;

        /// <summary>
        /// 关联的标签实体对象
        /// </summary>
        public virtual Tag Tag { get; set; } = null!;

    }

}
