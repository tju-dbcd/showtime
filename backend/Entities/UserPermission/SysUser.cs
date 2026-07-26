using ShowtimeBackend.Entities.Base;

namespace ShowtimeBackend.Entities.UserPermission;

public class SysUser : AuditableEntity
{
    public long UserId { get; set; }

    public string UserName { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public string? Nickname { get; set; }

    public string Phone { get; set; } = null!;

    public string? Email { get; set; }

    public long? OrgId { get; set; }

    public string UserType { get; set; } = "NORMAL";

    public byte Status { get; set; } = 1;

    public OrgStructure? Organization { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = [];

    public ICollection<UserBlacklist> BlacklistEntries { get; set; } = [];

    public ICollection<UserRealName> RealNames { get; set; } = [];

    public ICollection<OperationLog> OperationLogs { get; set; } = [];

    public ICollection<UserSession> Sessions { get; set; } = [];
}
