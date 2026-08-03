using Microsoft.AspNetCore.Mvc;
using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.SeatZone;
using ShowtimeBackend.Data;
using ShowtimeBackend.Services.SeatZone;

namespace ShowtimeBackend.Controllers.SeatZone;

/// <summary>
/// 管理端单个座位维护接口；批量编辑留给后续座位图编辑器。
/// </summary>
[ApiController]
[Route("api/admin")]
[Tags("Seat Zone Administration - Seats")]
public sealed class SeatsController : ControllerBase
{
    private readonly SeatAdminService _service;

    public SeatsController(AppDbContext db) => _service = new SeatAdminService(db);

    [HttpGet("seat-sections/{seatSectionId:long}/seats")]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<SeatResponse>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResponse<SeatResponse>>>> List(long seatSectionId, [FromQuery] SeatListQuery query, CancellationToken cancellationToken)
    {
        var result = await _service.ListSeatsAsync(seatSectionId, query, cancellationToken);
        return result.IsSuccess ? Ok(ApiResponse<PagedResponse<SeatResponse>>.Ok(result.Data!, "Seats retrieved.")) : ToFailure(result);
    }

    [HttpPost("seat-sections/{seatSectionId:long}/seats")]
    [ProducesResponseType(typeof(ApiResponse<SeatResponse>), StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<SeatResponse>>> Create(long seatSectionId, [FromBody] SeatRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CreateSeatAsync(seatSectionId, request, cancellationToken);
        if (!result.IsSuccess) return ToFailure(result);
        return CreatedAtAction(nameof(Get), new { seatId = result.Data!.SeatId }, ApiResponse<SeatResponse>.Ok(result.Data, "Seat created."));
    }

    [HttpGet("seats/{seatId:long}")]
    [ProducesResponseType(typeof(ApiResponse<SeatResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SeatResponse>>> Get(long seatId, CancellationToken cancellationToken)
    {
        var result = await _service.GetSeatAsync(seatId, cancellationToken);
        return result.IsSuccess ? Ok(ApiResponse<SeatResponse>.Ok(result.Data!, "Seat retrieved.")) : ToFailure(result);
    }

    [HttpPut("seats/{seatId:long}")]
    [ProducesResponseType(typeof(ApiResponse<SeatResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SeatResponse>>> Update(long seatId, [FromBody] SeatRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.UpdateSeatAsync(seatId, request, cancellationToken);
        return result.IsSuccess ? Ok(ApiResponse<SeatResponse>.Ok(result.Data!, "Seat updated.")) : ToFailure(result);
    }

    [HttpDelete("seats/{seatId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(long seatId, CancellationToken cancellationToken)
    {
        var result = await _service.DeleteSeatAsync(seatId, cancellationToken);
        return result.IsSuccess ? NoContent() : ToFailure(result);
    }

    private ActionResult ToFailure<T>(ServiceResult<T> result) => StatusCode(
        result.StatusCode ?? StatusCodes.Status500InternalServerError,
        ApiResponse<T>.Fail(result.Title!, result.Detail ?? result.Title!));
}
