namespace ShowtimeBackend.Entities.UserPermission;

public class RolePermission
{
    public long RolePermId { get; set; }

    public long RoleId { get; set; }

    public long PermissionId { get; set; }

    public Role Role { get; set; } = null!;

    public Permission Permission { get; set; } = null!;
}
