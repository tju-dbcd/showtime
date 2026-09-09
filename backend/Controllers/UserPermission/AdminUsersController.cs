using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.UserPermission;
using ShowtimeBackend.Services.UserPermission;

namespace ShowtimeBackend.Controllers.UserPermission;

/// <summary>管理端用户管理：分页查询、创建、删除（仅限 Admin 角色）。</summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/users")]
[Tags("Admin Users")]
public sealed class AdminUsersController(
    IAdminUserService adminUserService) : UserPermissionControllerBase
{
    /// <summary>分页查询用户列表（支持用户名/昵称/手机号/邮箱关键字与状态筛选）。</summary>
    [HttpGet]
    [ProducesResponseType(
        typeof(ApiResponse<PagedResponse<AdminUserResponse>>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ApiResponse<PagedResponse<AdminUserResponse>>),
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        typeof(ApiResponse<PagedResponse<AdminUserResponse>>),
        StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResponse<AdminUserResponse>>>> List(
        [FromQuery] AdminUserQueryRequest query,
        CancellationToken cancellationToken)
    {
        var result = await adminUserService.QueryAsync(query, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<PagedResponse<AdminUserResponse>>.Ok(
                result.Value!,
                "Users retrieved."))
            : FailureResponse<PagedResponse<AdminUserResponse>>(result);
    }

    /// <summary>创建用户（默认绑定启用的 USER 角色）。</summary>
    [HttpPost]
    [ProducesResponseType(
        typeof(ApiResponse<AdminUserResponse>),
        StatusCodes.Status201Created)]
    [ProducesResponseType(
        typeof(ApiResponse<AdminUserResponse>),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ApiResponse<AdminUserResponse>),
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        typeof(ApiResponse<AdminUserResponse>),
        StatusCodes.Status403Forbidden)]
    [ProducesResponseType(
        typeof(ApiResponse<AdminUserResponse>),
        StatusCodes.Status409Conflict)]
    [ProducesResponseType(
        typeof(ApiResponse<AdminUserResponse>),
        StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ApiResponse<AdminUserResponse>>> Create(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUser(out _, out var actor))
        {
            return UnauthorizedResponse<AdminUserResponse>();
        }

        var result = await adminUserService.CreateAsync(
            request,
            actor,
            cancellationToken);
        return result.IsSuccess
            ? StatusCode(
                StatusCodes.Status201Created,
                ApiResponse<AdminUserResponse>.Ok(
                    result.Value!,
                    "User created."))
            : FailureResponse<AdminUserResponse>(result);
    }

    /// <summary>删除用户（仅当无订单/票务/会话/实名等关联数据时可物理删除）。</summary>
    [HttpDelete("{userId:long}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(
        long userId,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUser(out _, out var actor))
        {
            return UnauthorizedResponse<bool>();
        }

        var result = await adminUserService.DeleteAsync(
            userId,
            actor,
            cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<bool>.Ok(true, "User deleted."))
            : FailureResponse<bool>(result);
    }

    private ActionResult<ApiResponse<T>> FailureResponse<T>(
        AdminUserResult<T> result)
    {
        var statusCode = result.Failure switch
        {
            AdminUserFailure.InvalidRequest => StatusCodes.Status400BadRequest,
            AdminUserFailure.NotFound => StatusCodes.Status404NotFound,
            AdminUserFailure.Conflict => StatusCodes.Status409Conflict,
            AdminUserFailure.Unavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError,
        };

        return StatusCode(
            statusCode,
            ApiResponse<T>.Fail(result.ErrorCode!, result.Message!));
    }
}
