using System.Collections.Generic;
using ShowtimeBackend.Entities.Base;

namespace ShowtimeBackend.Entities.ShowSessions
{
    /// <summary>
    /// 分类实体（支持父子层级结构）
    /// </summary>
    public class Category : AuditableEntity
    {
        /// <summary>
        /// 分类主键 CATEGORY_ID NUMBER(19,0)
        /// </summary>
        public long CategoryId { get; set; }

        /// <summary>
        /// 分类名称 CATEGORY_NAME VARCHAR2(50 CHAR)
        /// </summary>
        public string CategoryName { get; set; } = string.Empty;

        /// <summary>
        /// 父分类 ID PARENT_ID NUMBER(19,0)
        /// </summary>
        public long? ParentId { get; set; }

        /// <summary>
        /// 排序号 SORT_ORDER NUMBER(5,0) 默认 0
        /// </summary>
        public int SortOrder { get; set; } = 0;

        /// <summary>
        /// 状态 STATUS NUMBER(1,0) 默认 1
        /// 允许值: 0 (禁用/停用), 1 (启用/正常)
        /// </summary>
        public int Status { get; set; } = 1;

        // ================= 导航属性 =================
        /// <summary>
        /// 父分类实体
        /// </summary>
        public virtual Category? ParentCategory { get; set; }

        /// <summary>
        /// 子分类列表
        /// </summary>
        public virtual ICollection<Category> SubCategories { get; set; } = new List<Category>();
    }
}
