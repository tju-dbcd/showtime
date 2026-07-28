using ShowtimeBackend.Entities.Base;

namespace ShowtimeBackend.Entities.UserPermission;

public class OrgStructure : AuditableEntity
{
    public long OrgId { get; set; }

    public long? ParentId { get; set; }

    public string OrgCode { get; set; } = null!;

    public string OrgName { get; set; } = null!;

    public string OrgType { get; set; } = "DEPT";

    public int SortOrder { get; set; }

    public bool Status { get; set; } = true;

    public OrgStructure? Parent { get; set; }

    public ICollection<OrgStructure> Children { get; set; } = [];

    public ICollection<SysUser> Users { get; set; } = [];
}
