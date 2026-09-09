namespace ShowtimeBackend.DTOs.UserPermission;

/// <summary>
/// 管理端用户分页查询；Keyword 模糊匹配用户名/昵称/手机号/邮箱，Status 精确匹配账号状态。
/// </summary>
public sealed record AdminUserQueryRequest(
    string? Keyword = null,
    byte? Status = null,
    int PageIndex = 1,
    int PageSize = 10);

/// <summary>管理端用户信息（含账号状态、类型与角色）。</summary>
public sealed record AdminUserResponse(
    long UserId,
    string UserName,
    string? Nickname,
    string Phone,
    string? Email,
    string? AvatarUrl,
    string UserType,
    byte Status,
    IReadOnlyList<string> Roles,
    DateTime CreateTime,
    DateTime UpdateTime);
