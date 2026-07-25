using ShowtimeBackend.Entities.Base;

namespace ShowtimeBackend.Entities.UserPermission;

public class UserBlacklist : AuditableEntity
{
    public long BlacklistId { get; set; }

    public long UserId { get; set; }

    public long? ShowId { get; set; }

    public string RiskType { get; set; } = null!;

    public int RiskScore { get; set; }

    public DateTime StartTime { get; set; }

    public DateTime? EndTime { get; set; }

    public bool IsPermanent { get; set; }

    public string? Reason { get; set; }

    public bool Status { get; set; } = true;

    public SysUser User { get; set; } = null!;
}
