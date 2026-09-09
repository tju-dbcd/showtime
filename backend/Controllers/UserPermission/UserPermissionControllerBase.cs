using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using ShowtimeBackend.Common;

namespace ShowtimeBackend.Controllers.UserPermission;

public abstract class UserPermissionControllerBase : ControllerBase
{
    protected bool TryGetCurrentUser(out long userId, out string actor)
    {
        var subject = User.FindFirstValue("sub") ??
                      User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(subject, out userId) || userId <= 0)
        {
            actor = string.Empty;
            return false;
        }

        actor = User.Identity?.Name ?? userId.ToString();
        return true;
    }

    protected ActionResult<ApiResponse<T>> UnauthorizedResponse<T>() => Unauthorized(
        ApiResponse<T>.Fail(
            "AUTH_REQUIRED",
            "A valid authenticated user is required."));
}
