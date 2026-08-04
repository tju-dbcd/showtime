using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.SeatZone;
using ShowtimeBackend.Data;
using ShowtimeBackend.Services.SeatZone;

namespace ShowtimeBackend.Controllers.SeatZone;

/// <summary>
/// 管理端座位图维护接口（仅 Admin 角色）。
/// </summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/seat-maps")]
[Tags("Seat Zone Administration - Seat Maps")]
public sealed class SeatMapsController : ControllerBase
{
    private readonly SeatMapAdminService _service;

    public SeatMapsController(AppDbContext db) => _service = new SeatMapAdminService(db);

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<SeatMapResponse>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResponse<SeatMapResponse>>>> List([FromQuery] SeatMapListQuery query, CancellationToken cancellationToken)
    {
        var result = await _service.ListMapsAsync(query, cancellationToken);
        return result.IsSuccess ? Ok(ApiResponse<PagedResponse<SeatMapResponse>>.Ok(result.Data!, "Seat maps retrieved.")) : ToFailure(result);
    }

    [HttpGet("{seatMapId:long}")]
    [ProducesResponseType(typeof(ApiResponse<SeatMapResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SeatMapResponse>>> Get(long seatMapId, CancellationToken cancellationToken)
    {
        var result = await _service.GetMapAsync(seatMapId, cancellationToken);
        return result.IsSuccess ? Ok(ApiResponse<SeatMapResponse>.Ok(result.Data!, "Seat map retrieved.")) : ToFailure(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<SeatMapResponse>), StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<SeatMapResponse>>> Create([FromBody] SeatMapRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CreateMapAsync(request, cancellationToken);
        if (!result.IsSuccess) return ToFailure(result);
        return CreatedAtAction(nameof(Get), new { seatMapId = result.Data!.SeatMapId }, ApiResponse<SeatMapResponse>.Ok(result.Data, "Seat map created."));
    }

    [HttpPut("{seatMapId:long}")]
    [ProducesResponseType(typeof(ApiResponse<SeatMapResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SeatMapResponse>>> Update(long seatMapId, [FromBody] SeatMapRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.UpdateMapAsync(seatMapId, request, cancellationToken);
        return result.IsSuccess ? Ok(ApiResponse<SeatMapResponse>.Ok(result.Data!, "Seat map updated.")) : ToFailure(result);
    }

    [HttpDelete("{seatMapId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(long seatMapId, CancellationToken cancellationToken)
    {
        var result = await _service.DeleteMapAsync(seatMapId, cancellationToken);
        return result.IsSuccess ? NoContent() : ToFailure(result);
    }

    private ActionResult ToFailure<T>(ServiceResult<T> result) => StatusCode(
        result.StatusCode ?? StatusCodes.Status500InternalServerError,
        ApiResponse<T>.Fail(result.Title!, result.Detail ?? result.Title!));
}
