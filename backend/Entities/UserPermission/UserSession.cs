using ShowtimeBackend.Entities.Base;

namespace ShowtimeBackend.Entities.UserPermission;

public class UserSession : AuditableEntity
{
    public long UserSessionId { get; set; }

    public long UserId { get; set; }

    public string SessionToken { get; set; } = null!;

    public DateTime LoginTime { get; set; }

    public DateTime ExpireTime { get; set; }

    public DateTime? LogoutTime { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public bool RiskFlag { get; set; }

    public string Status { get; set; } = "ACTIVE";

    public SysUser User { get; set; } = null!;
}
