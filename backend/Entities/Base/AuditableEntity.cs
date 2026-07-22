namespace ShowtimeBackend.Entities.Base;

/// <summary>
/// 审计字段基类；
/// EF Core 不在插入/更新时设置这些值，由数据库触发器自动维护。
/// </summary>
public abstract class AuditableEntity
{
    /// <summary>创建时间 </summary>
    public DateTime CreateTime { get; set; }

    /// <summary>更新时间</summary>
    public DateTime UpdateTime { get; set; }

    /// <summary>创建人用户名</summary>
    public string? CreateBy { get; set; }

    /// <summary>最后修改人用户名</summary>
    public string? UpdateBy { get; set; }
}
