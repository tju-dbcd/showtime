using ShowtimeBackend.Entities.Base;

namespace ShowtimeBackend.Entities.UserPermission;

public class OperationLog : AuditableEntity
{
    public long LogId { get; set; }

    public long? UserId { get; set; }

    public string? UserName { get; set; }

    public long? ShowId { get; set; }

    public string OperationModule { get; set; } = null!;

    public string OperationType { get; set; } = null!;

    public string? RequestUrl { get; set; }

    public string? RequestParams { get; set; }

    public string? ResponseResult { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public long? CostTime { get; set; }

    public bool Status { get; set; } = true;

    public string? ErrorMsg { get; set; }

    public SysUser? User { get; set; }
}
