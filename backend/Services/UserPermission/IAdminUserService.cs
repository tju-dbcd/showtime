using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.UserPermission;

namespace ShowtimeBackend.Services.UserPermission;

public interface IAdminUserService
{
    /// <summary>管理端分页查询用户（Keyword 模糊匹配用户名/昵称/手机号/邮箱，可按状态过滤）。</summary>
    Task<AdminUserResult<PagedResponse<AdminUserResponse>>> QueryAsync(
        AdminUserQueryRequest query,
        CancellationToken cancellationToken);

    /// <summary>管理端创建用户：默认绑定启用的 USER 角色，返回完整用户信息。</summary>
    Task<AdminUserResult<AdminUserResponse>> CreateAsync(
        RegisterRequest request,
        string operatorName,
        CancellationToken cancellationToken);

    /// <summary>
    /// 管理端删除用户：仅当用户没有订单/票务/会话/实名/黑名单等关联数据时物理删除
    /// （USER_ROLE 由外键级联清理）；有关联数据时返回冲突。
    /// </summary>
    Task<AdminUserResult<bool>> DeleteAsync(
        long userId,
        string operatorName,
        CancellationToken cancellationToken);
}
