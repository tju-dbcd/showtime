using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.SeatZone;
using ShowtimeBackend.Data;
using ShowtimeBackend.Services.SeatZone;

namespace ShowtimeBackend.Controllers.SeatZone;

/// <summary>
/// 面向用户端选座页的只读座位图接口（游客可浏览场次与座位布局）。
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/sessions")]
[Tags("Sessions")]
public sealed class SessionSeatMapController : ControllerBase
{
    private readonly SessionSeatMapQueryService _service;

    public SessionSeatMapController(AppDbContext db, TimeProvider timeProvider) =>
        _service = new SessionSeatMapQueryService(db, timeProvider);

    [HttpGet("{sessionId:long}/seat-map")]
    [ProducesResponseType(typeof(ApiResponse<SessionSeatMapDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SessionSeatMapDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<SessionSeatMapDto>>> Get(long sessionId, CancellationToken cancellationToken)
    {
        var result = await _service.GetAsync(sessionId, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<SessionSeatMapDto>.Ok(result.Data!, "Session seat map retrieved."))
            : StatusCode(
                result.StatusCode ?? StatusCodes.Status500InternalServerError,
                ApiResponse<SessionSeatMapDto>.Fail(result.Title!, result.Detail ?? result.Title!));
    }
}
