using Microsoft.AspNetCore.Mvc;
using ShowtimeBackend.Contracts.Common;
using ShowtimeBackend.Contracts.SeatZone;
using ShowtimeBackend.Data;
using ShowtimeBackend.Features.SeatZone.Services;

namespace ShowtimeBackend.Controllers.Admin;

/// <summary>
/// 管理端座位图维护接口；实际权限拦截由后续统一 JWT 模块接入。
/// </summary>
[ApiController]
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
        return result.IsSuccess ? Ok(new ApiResponse<PagedResponse<SeatMapResponse>>(result.Data!)) : ToProblem(result);
    }

    [HttpGet("{seatMapId:long}")]
    [ProducesResponseType(typeof(ApiResponse<SeatMapResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SeatMapResponse>>> Get(long seatMapId, CancellationToken cancellationToken)
    {
        var result = await _service.GetMapAsync(seatMapId, cancellationToken);
        return result.IsSuccess ? Ok(new ApiResponse<SeatMapResponse>(result.Data!)) : ToProblem(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<SeatMapResponse>), StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<SeatMapResponse>>> Create([FromBody] SeatMapRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CreateMapAsync(request, cancellationToken);
        if (!result.IsSuccess) return ToProblem(result);
        return CreatedAtAction(nameof(Get), new { seatMapId = result.Data!.SeatMapId }, new ApiResponse<SeatMapResponse>(result.Data));
    }

    [HttpPut("{seatMapId:long}")]
    [ProducesResponseType(typeof(ApiResponse<SeatMapResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SeatMapResponse>>> Update(long seatMapId, [FromBody] SeatMapRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.UpdateMapAsync(seatMapId, request, cancellationToken);
        return result.IsSuccess ? Ok(new ApiResponse<SeatMapResponse>(result.Data!)) : ToProblem(result);
    }

    [HttpDelete("{seatMapId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(long seatMapId, CancellationToken cancellationToken)
    {
        var result = await _service.DeleteMapAsync(seatMapId, cancellationToken);
        return result.IsSuccess ? NoContent() : ToProblem(result);
    }

    private ActionResult ToProblem<T>(ServiceResult<T> result) => Problem(statusCode: result.StatusCode, title: result.Title, detail: result.Detail);
}
