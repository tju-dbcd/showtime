namespace ShowtimeBackend.Entities.UserPermission;

public class UserRole
{
    public long UserRoleId { get; set; }

    public long UserId { get; set; }

    public long RoleId { get; set; }

    public SysUser User { get; set; } = null!;

    public Role Role { get; set; } = null!;
}
