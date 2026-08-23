using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.ShowSessionChange;
using ShowtimeBackend.DTOs.ShowSessionDto;
using ShowtimeBackend.Services.ShowSession;
using ShowtimeBackend.DTOs.Show;

namespace ShowtimeBackend.Controllers.ShowSession.Admin;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminShowSessionController : ControllerBase
{
    private readonly IAdminShowSessionService _adminService;

    public AdminShowSessionController(IAdminShowSessionService adminService)
    {
        _adminService = adminService;
    }

    /// <summary>
    /// 获取指定演出下的所有排期场次
    /// </summary>
    [HttpGet("shows/{showId:long}/sessions")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<ShowSessionDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IEnumerable<ShowSessionDto>>>> GetAdminSessions(
        [FromRoute] long showId,
        CancellationToken cancellationToken)
    {
        var sessions = await _adminService.GetAdminSessionsByShowIdAsync(showId, cancellationToken);
        return Ok(ApiResponse<IEnumerable<ShowSessionDto>>.Ok(sessions, "获取管理端场次列表成功"));
    }


    /// <summary>
    /// 为指定演出创建/排布场次
    /// </summary>
    [HttpPost("shows/{showId:long}/sessions")]
    [ProducesResponseType(typeof(ApiResponse<ShowSessionDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<ShowSessionDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<ShowSessionDto>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<ShowSessionDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<ShowSessionDto>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<ShowSessionDto>>> CreateSession(
        [FromRoute] long showId,
        [FromBody] CreateShowSessionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var createdSession = await _adminService.CreateSessionAsync(showId, request, cancellationToken);

            // 包装成功响应
            var response = ApiResponse<ShowSessionDto>.Ok(createdSession, "场次排布成功");

            // 修复（P0）：原 CreatedAtAction("GetSessionById", ...) 引用了不存在的 action，
            // 导致场次已 INSERT 但响应生成时抛 No route matches（HTTP 500 且客户端无法感知数据已落库）。
            // Location 指向真实存在的客户端场次列表资源；响应体已包含完整场次信息。
            return Created(
                $"/api/client/shows/{showId}/sessions",
                response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<ShowSessionDto>.Fail("INVALID_ARGUMENT", ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ApiResponse<ShowSessionDto>.Fail("OPERATION_CONFLICT", ex.Message));
        }
    }

    /// <summary>
    /// 配置或覆盖更新场次票价策略
    /// </summary>
    [HttpPost("sessions/{sessionId:long}/pricing-strategies")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<object>>> ConfigurePriceStrategies(
        [FromRoute] long sessionId,
        [FromBody] IEnumerable<CreatePriceStrategyRequest> requests,
        CancellationToken cancellationToken)
    {
        try
        {
            await _adminService.ConfigurePriceStrategiesAsync(sessionId, requests, cancellationToken);
            return Ok(ApiResponse<object>.Ok(null!, "票价策略配置成功"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", ex.Message));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<object>.Fail("INVALID_ARGUMENT", ex.Message));
        }
    }

    /// <summary>
    /// 变更场次状态（如手动停售、恢复或下架）
    /// </summary>
    [HttpPut("sessions/{sessionId:long}/status")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<object>>> UpdateSessionStatus(
        [FromRoute] long sessionId,
        [FromBody] UpdateSessionStatusRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _adminService.UpdateSessionStatusAsync(sessionId, request.Status, cancellationToken);
            return Ok(ApiResponse<object>.Ok(null!, $"场次状态已成功更新为 {request.Status}"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", ex.Message));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<object>.Fail("INVALID_ARGUMENT", ex.Message));
        }
    }
}

[ApiController]
[Route("api/admin/shows")]
[Authorize(Roles = "Admin")]
public class AdminShowController : ControllerBase
{
    private readonly IAdminShowService _showService;

    public AdminShowController(IAdminShowService showService)
    {
        _showService = showService;
    }

    /// <summary>
    /// 创建/发布演出基础信息（获取 showId）
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<ShowDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<ShowDto>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<ShowDto>>> CreateShow(
        [FromBody] CreateShowRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var createdShow = await _showService.CreateShowAsync(request, cancellationToken);
            return Created($"/api/admin/shows/{createdShow.ShowId}", ApiResponse<ShowDto>.Ok(createdShow, "演出创建成功"));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<ShowDto>.Fail("INVALID_ARGUMENT", ex.Message));
        }
    }

    /// <summary>
    /// 分页查询演出列表
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<ShowDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResponse<ShowDto>>>> GetShows(
        [FromQuery] ShowQueryRequest query,
        CancellationToken cancellationToken)
    {
        var result = await _showService.GetShowsAsync(query, cancellationToken);
        return Ok(ApiResponse<PagedResponse<ShowDto>>.Ok(result, "获取演出列表成功"));
    }

    /// <summary>
    /// 获取演出详情
    /// </summary>
    [HttpGet("{showId:long}")]
    [ProducesResponseType(typeof(ApiResponse<ShowDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ShowDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ShowDto>>> GetShowById(
        [FromRoute] long showId,
        CancellationToken cancellationToken)
    {
        try
        {
            var show = await _showService.GetShowByIdAsync(showId, cancellationToken);
            return Ok(ApiResponse<ShowDto>.Ok(show, "获取演出详情成功"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<ShowDto>.Fail("NOT_FOUND", ex.Message));
        }
    }

    /// <summary>
    /// 更新演出信息
    /// </summary>
    [HttpPut("{showId:long}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<object>>> UpdateShow(
        [FromRoute] long showId,
        [FromBody] UpdateShowRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _showService.UpdateShowAsync(showId, request, cancellationToken);
            return Ok(ApiResponse<object>.Ok(null!, "演出更新成功"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", ex.Message));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<object>.Fail("INVALID_ARGUMENT", ex.Message));
        }
    }

    /// <summary>
    /// 删除指定演出
    /// </summary>
    [HttpDelete("{showId:long}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<object>>> DeleteShow(
        [FromRoute] long showId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _showService.DeleteShowAsync(showId, cancellationToken);
            return Ok(ApiResponse<object>.Ok(null!, "演出删除成功"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ApiResponse<object>.Fail("OPERATION_CONFLICT", ex.Message));
        }
    }
}
