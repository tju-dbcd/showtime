using System.Security.Claims;
using ShowtimeBackend.Common;
using Microsoft.AspNetCore.Mvc;
using ShowtimeBackend.Services.OrderTicket;

namespace ShowtimeBackend.Controllers.OrderTicket;

public abstract class OrderTicketControllerBase : ControllerBase
{
    protected bool TryGetCurrentUser(out long userId, out string actor)
    {
        var subject = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
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

    protected ActionResult<ApiResponse<T>> FailureResponse<T>(OrderTicketResult<T> result)
    {
        var statusCode = result.Failure switch
        {
            OrderTicketFailure.InvalidRequest => StatusCodes.Status400BadRequest,
            OrderTicketFailure.NotFound => StatusCodes.Status404NotFound,
            OrderTicketFailure.Conflict => StatusCodes.Status409Conflict,
            OrderTicketFailure.Internal => StatusCodes.Status500InternalServerError,
            _ => StatusCodes.Status500InternalServerError
        };

        return StatusCode(
            statusCode,
            ApiResponse<T>.Fail(result.ErrorCode!, result.Message!));
    }
}
