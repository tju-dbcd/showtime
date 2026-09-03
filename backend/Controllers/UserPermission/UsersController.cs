using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.UserPermission;
using ShowtimeBackend.Services.UserPermission;

namespace ShowtimeBackend.Controllers.UserPermission;

/// <summary>当前用户资料相关接口（头像等），JWT 中 sub 即用户 ID。</summary>
[ApiController]
[Route("api/users")]
[Authorize]
public sealed class UsersController(IAuthService authService) : ControllerBase
{
    [HttpPut("me/avatar")]
    [ProducesResponseType(typeof(ApiResponse<UserResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<UserResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<UserResponse>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<UserResponse>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<UserResponse>>> UpdateAvatar(
        UpdateAvatarRequest request,
        CancellationToken cancellationToken)
    {
        var subject = User.FindFirstValue("sub")
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(subject, out var userId) || userId <= 0)
        {
            return Unauthorized(
                ApiResponse<UserResponse>.Fail(
                    "AUTH_REQUIRED",
                    "A valid authenticated user is required."));
        }

        var result = await authService.UpdateAvatarAsync(
            userId,
            request.AvatarUrl ?? string.Empty,
            cancellationToken);
        if (result.IsSuccess)
        {
            return Ok(
                ApiResponse<UserResponse>.Ok(
                    result.Value!,
                    "Avatar updated successfully."));
        }

        var (statusCode, code, message) = result.Failure switch
        {
            AuthFailure.UserNotFound => (
                StatusCodes.Status404NotFound,
                "USER_NOT_FOUND",
                "The user does not exist."),
            AuthFailure.InvalidAvatarUrl => (
                StatusCodes.Status400BadRequest,
                "INVALID_AVATAR_URL",
                "The avatar URL must be an absolute http(s) URL up to 500 characters."),
            _ => throw new ArgumentOutOfRangeException(
                nameof(result.Failure),
                result.Failure,
                "Unknown avatar update failure."),
        };

        return StatusCode(
            statusCode,
            ApiResponse<UserResponse>.Fail(code, message));
    }
}
