using ShowtimeBackend.Entities.Base;

namespace ShowtimeBackend.Entities.UserPermission;

public class Permission : AuditableEntity
{
    public long PermissionId { get; set; }

    public string PermCode { get; set; } = null!;

    public string PermName { get; set; } = null!;

    public string ResourceType { get; set; } = null!;

    public long? ParentId { get; set; }

    public int SortOrder { get; set; }

    public bool Status { get; set; } = true;

    public Permission? Parent { get; set; }

    public ICollection<Permission> Children { get; set; } = [];

    public ICollection<RolePermission> RolePermissions { get; set; } = [];
}
