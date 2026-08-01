using Microsoft.AspNetCore.Mvc;
using ShowtimeBackend.Contracts.Common;
using ShowtimeBackend.Contracts.SeatZone;
using ShowtimeBackend.Data;
using ShowtimeBackend.Features.SeatZone.Services;

namespace ShowtimeBackend.Controllers;

/// <summary>
/// 面向用户端选座页的只读座位图接口。
/// </summary>
[ApiController]
[Route("api/sessions")]
[Tags("Sessions")]
public sealed class SessionSeatMapController : ControllerBase
{
    private readonly SessionSeatMapQueryService _service;

    public SessionSeatMapController(AppDbContext db) => _service = new SessionSeatMapQueryService(db);

    [HttpGet("{sessionId:long}/seat-map")]
    [ProducesResponseType(typeof(ApiResponse<SessionSeatMapDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<SessionSeatMapDto>>> Get(long sessionId, CancellationToken cancellationToken)
    {
        var result = await _service.GetAsync(sessionId, cancellationToken);
        return result.IsSuccess
            ? Ok(new ApiResponse<SessionSeatMapDto>(result.Data!))
            : Problem(statusCode: result.StatusCode, title: result.Title, detail: result.Detail);
    }
}
