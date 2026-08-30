using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.SeatZone;
using ShowtimeBackend.Services.SeatZone;

namespace ShowtimeBackend.Controllers.SeatZone;

/// <summary>
/// 用户选座过程中使用的临时锁定与释放接口。
/// </summary>
[ApiController]
[Authorize]
[Route("api/sessions/{sessionId:long}/seat-locks")]
[Tags("Seat Locks")]
public sealed class SeatLocksController(ISeatLockService seatLockService) : ControllerBase
{
    /// <summary>
    /// 批量锁定座位；任一座位不可锁时整个请求失败。
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<SeatLockBatchResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<SeatLockBatchResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<SeatLockBatchResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<SeatLockBatchResponse>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<SeatLockBatchResponse>>> Lock(
        long sessionId,
        [FromBody] SeatLockBatchRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUser(out var userId, out var actor))
        {
            return UnauthorizedResponse<SeatLockBatchResponse>();
        }

        var result = await seatLockService.LockAsync(
            userId,
            actor,
            sessionId,
            request,
            cancellationToken);
        return result.IsSuccess
            ? StatusCode(
                StatusCodes.Status201Created,
                ApiResponse<SeatLockBatchResponse>.Ok(result.Value!, "Seats locked."))
            : FailureResponse(result);
    }

    /// <summary>
    /// 批量释放当前用户仍然有效的座位锁。
    /// </summary>
    [HttpPost("release")]
    [ProducesResponseType(typeof(ApiResponse<SeatLockReleaseResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SeatLockReleaseResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<SeatLockReleaseResponse>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<SeatLockReleaseResponse>>> Release(
        long sessionId,
        [FromBody] SeatLockReleaseRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUser(out var userId, out var actor))
        {
            return UnauthorizedResponse<SeatLockReleaseResponse>();
        }

        var result = await seatLockService.ReleaseAsync(
            userId,
            actor,
            sessionId,
            request,
            cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<SeatLockReleaseResponse>.Ok(
                result.Value!,
                "Seat locks released."))
            : FailureResponse(result);
    }

    private bool TryGetCurrentUser(out long userId, out string actor)
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

    private ActionResult<ApiResponse<T>> UnauthorizedResponse<T>() => Unauthorized(
        ApiResponse<T>.Fail(
            "AUTH_REQUIRED",
            "A valid authenticated user is required."));

    private ActionResult<ApiResponse<T>> FailureResponse<T>(SeatZoneResult<T> result)
    {
        var statusCode = result.Failure switch
        {
            SeatZoneFailure.InvalidRequest => StatusCodes.Status400BadRequest,
            SeatZoneFailure.NotFound => StatusCodes.Status404NotFound,
            SeatZoneFailure.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };

        return StatusCode(
            statusCode,
            ApiResponse<T>.Fail(result.ErrorCode!, result.Message!));
    }
}
