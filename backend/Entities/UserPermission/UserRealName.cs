using ShowtimeBackend.Entities.Base;

namespace ShowtimeBackend.Entities.UserPermission;

public class UserRealName : AuditableEntity
{
    public long RealNameId { get; set; }

    public long UserId { get; set; }

    public string RealName { get; set; } = null!;

    public string IdCardNo { get; set; } = null!;

    public bool IsDefault { get; set; }

    public bool IsVerified { get; set; }

    public SysUser User { get; set; } = null!;
}
