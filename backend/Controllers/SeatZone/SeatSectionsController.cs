using Microsoft.AspNetCore.Mvc;
using ShowtimeBackend.DTOs;
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
        return result.IsSuccess ? Ok(new ApiResponse<PagedResponse<SeatSectionResponse>>(result.Data!)) : ToProblem(result);
    }

    [HttpPost("seat-maps/{seatMapId:long}/sections")]
    [ProducesResponseType(typeof(ApiResponse<SeatSectionResponse>), StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<SeatSectionResponse>>> Create(long seatMapId, [FromBody] SeatSectionRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CreateSectionAsync(seatMapId, request, cancellationToken);
        if (!result.IsSuccess) return ToProblem(result);
        return CreatedAtAction(nameof(Get), new { seatSectionId = result.Data!.SeatSectionId }, new ApiResponse<SeatSectionResponse>(result.Data));
    }

    [HttpGet("seat-sections/{seatSectionId:long}")]
    [ProducesResponseType(typeof(ApiResponse<SeatSectionResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SeatSectionResponse>>> Get(long seatSectionId, CancellationToken cancellationToken)
    {
        var result = await _service.GetSectionAsync(seatSectionId, cancellationToken);
        return result.IsSuccess ? Ok(new ApiResponse<SeatSectionResponse>(result.Data!)) : ToProblem(result);
    }

    [HttpPut("seat-sections/{seatSectionId:long}")]
    [ProducesResponseType(typeof(ApiResponse<SeatSectionResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SeatSectionResponse>>> Update(long seatSectionId, [FromBody] SeatSectionRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.UpdateSectionAsync(seatSectionId, request, cancellationToken);
        return result.IsSuccess ? Ok(new ApiResponse<SeatSectionResponse>(result.Data!)) : ToProblem(result);
    }

    [HttpDelete("seat-sections/{seatSectionId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(long seatSectionId, CancellationToken cancellationToken)
    {
        var result = await _service.DeleteSectionAsync(seatSectionId, cancellationToken);
        return result.IsSuccess ? NoContent() : ToProblem(result);
    }

    private ActionResult ToProblem<T>(ServiceResult<T> result) => Problem(statusCode: result.StatusCode, title: result.Title, detail: result.Detail);
}
