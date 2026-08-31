using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.SeatZone;
using ShowtimeBackend.Data;
using ShowtimeBackend.Services.SeatZone;

namespace ShowtimeBackend.Controllers.SeatZone;

/// <summary>
/// 管理端座位维护接口（仅 Admin 角色）。
/// </summary>
[ApiController]
[Authorize(Roles = "Admin")]
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

    /// <summary>
    /// 批量修改同一票区内座位的可编辑属性。
    /// </summary>
    [HttpPatch("seat-sections/{seatSectionId:long}/seats")]
    [ProducesResponseType(typeof(ApiResponse<SeatBatchUpdateResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SeatBatchUpdateResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<SeatBatchUpdateResponse>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<SeatBatchUpdateResponse>>> UpdateBatch(
        long seatSectionId,
        [FromBody] SeatBatchUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.UpdateSeatsAsync(
            seatSectionId,
            request,
            cancellationToken);

        return result.IsSuccess
            ? Ok(ApiResponse<SeatBatchUpdateResponse>.Ok(
                result.Data!,
                "Seats updated."))
            : ToFailure(result);
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
