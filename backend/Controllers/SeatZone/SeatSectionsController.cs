using Microsoft.AspNetCore.Mvc;
using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.SeatZone;
using ShowtimeBackend.Data;
using ShowtimeBackend.Services.SeatZone;

namespace ShowtimeBackend.Controllers.SeatZone;

/// <summary>
/// 管理端票区维护接口，票区从属于一张座位图。
/// </summary>
[ApiController]
[Route("api/admin")]
[Tags("Seat Zone Administration - Seat Sections")]
public sealed class SeatSectionsController : ControllerBase
{
    private readonly SeatMapAdminService _service;

    public SeatSectionsController(AppDbContext db) => _service = new SeatMapAdminService(db);

    [HttpGet("seat-maps/{seatMapId:long}/sections")]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<SeatSectionResponse>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResponse<SeatSectionResponse>>>> List(long seatMapId, [FromQuery] SeatSectionListQuery query, CancellationToken cancellationToken)
    {
        var result = await _service.ListSectionsAsync(seatMapId, query, cancellationToken);
        return result.IsSuccess ? Ok(ApiResponse<PagedResponse<SeatSectionResponse>>.Ok(result.Data!, "Seat sections retrieved.")) : ToFailure(result);
    }

    [HttpPost("seat-maps/{seatMapId:long}/sections")]
    [ProducesResponseType(typeof(ApiResponse<SeatSectionResponse>), StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<SeatSectionResponse>>> Create(long seatMapId, [FromBody] SeatSectionRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CreateSectionAsync(seatMapId, request, cancellationToken);
        if (!result.IsSuccess) return ToFailure(result);
        return CreatedAtAction(nameof(Get), new { seatSectionId = result.Data!.SeatSectionId }, ApiResponse<SeatSectionResponse>.Ok(result.Data, "Seat section created."));
    }

    [HttpGet("seat-sections/{seatSectionId:long}")]
    [ProducesResponseType(typeof(ApiResponse<SeatSectionResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SeatSectionResponse>>> Get(long seatSectionId, CancellationToken cancellationToken)
    {
        var result = await _service.GetSectionAsync(seatSectionId, cancellationToken);
        return result.IsSuccess ? Ok(ApiResponse<SeatSectionResponse>.Ok(result.Data!, "Seat section retrieved.")) : ToFailure(result);
    }

    [HttpPut("seat-sections/{seatSectionId:long}")]
    [ProducesResponseType(typeof(ApiResponse<SeatSectionResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SeatSectionResponse>>> Update(long seatSectionId, [FromBody] SeatSectionRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.UpdateSectionAsync(seatSectionId, request, cancellationToken);
        return result.IsSuccess ? Ok(ApiResponse<SeatSectionResponse>.Ok(result.Data!, "Seat section updated.")) : ToFailure(result);
    }

    [HttpDelete("seat-sections/{seatSectionId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(long seatSectionId, CancellationToken cancellationToken)
    {
        var result = await _service.DeleteSectionAsync(seatSectionId, cancellationToken);
        return result.IsSuccess ? NoContent() : ToFailure(result);
    }

    private ActionResult ToFailure<T>(ServiceResult<T> result) => StatusCode(
        result.StatusCode ?? StatusCodes.Status500InternalServerError,
        ApiResponse<T>.Fail(result.Title!, result.Detail ?? result.Title!));
}
