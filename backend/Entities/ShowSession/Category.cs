//namespace ShowtimeBackend.Entities.Session // 由base派生出session类管理演出场次相关数据

using ShowtimeBackend.Entities.Base;

namespace ShowtimeBackend.Entities.ShowSessions
{
    /// <summary>
    /// 演出分类实体（支持无限级树形分类）
    /// </summary>
    public class Category : AuditableEntity
    {
        /// <summary>
        /// 分类唯一标识，主键，自增
        /// </summary>
        public long CategoryId { get; set; }

        /// <summary>
        /// 分类名称
        /// </summary>
        public string CategoryName { get; set; } = string.Empty;

        /// <summary>
        /// 父分类 ID（外键，可为空：为空代表顶级分类）
        /// </summary>
        public long? ParentId { get; set; }

        /// <summary>
        /// 排序顺序
        /// </summary>
        public int? SortOrder { get; set; } = 0;

        /// <summary>
        /// 状态：0-禁用，1-启用
        /// </summary>
        public int Status { get; set; } = 1;


        
        #region 自关联导航属性 (Tree Navigation Properties)

        /// <summary>
        /// 父级分类导航属性（多对一）
        /// </summary>
        public virtual Category? Parent { get; set; }

        /// <summary>
        /// 子级分类集合导航属性（一对多）
        /// </summary>
        public virtual ICollection<Category> Children { get; set; } = new List<Category>();

        #endregion
    }
}
