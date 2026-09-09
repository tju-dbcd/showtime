using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Common;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.SeatZone;

namespace ShowtimeBackend.Controllers.SeatZone;

/// <summary>
/// 管理端场馆只读接口（座位图新建/编辑弹窗选择场馆，避免硬编码场馆 ID）
/// </summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/venues")]
[Tags("Seat Zone Administration - Venues")]
public sealed class AdminVenuesController(AppDbContext db) : ControllerBase
{
    /// <summary>
    /// 获取全部启用中的场馆，按场馆 ID 升序。
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<VenueResponse>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IEnumerable<VenueResponse>>>> List(
        CancellationToken cancellationToken)
    {
        var venues = await db.Venues
            .AsNoTracking()
            .Where(item => item.Status == "ENABLED")
            .OrderBy(item => item.VenueId)
            .Select(item => new VenueResponse(
                item.VenueId,
                item.VenueName,
                item.Address,
                item.ContactPhone,
                item.Status,
                item.Remark))
            .ToListAsync(cancellationToken);
        return Ok(ApiResponse<IEnumerable<VenueResponse>>.Ok(venues, "Venues retrieved."));
    }
}
