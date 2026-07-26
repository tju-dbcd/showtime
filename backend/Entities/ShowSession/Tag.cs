//namespace ShowtimeBackend.Entities.Session // 由base派生出session类管理演出场次相关数据

using global::ShowtimeBackend.Entities.Base;

namespace ShowtimeBackend.Entities.ShowSessions
{
    /// <summary>
    /// 标签信息实体
    /// </summary>
    public class Tag : AuditableEntity
    {
        /// <summary>
        /// 标签唯一标识，主键，自增
        /// </summary>
        public long TagId { get; set; }

        /// <summary>
        /// 标签名称
        /// </summary>
        public string TagName { get; set; } = string.Empty;

        /// <summary>
        /// 标签颜色（十六进制色值，如 #FF5733）
        /// </summary>
        public string? Color { get; set; }

        /// <summary>
        /// 状态：0-禁用，1-启用
        /// </summary>
        public int Status { get; set; } = 1;

    }
}
