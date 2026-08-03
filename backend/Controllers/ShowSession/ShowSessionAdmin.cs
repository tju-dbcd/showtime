using Microsoft.AspNetCore.Mvc;
using ShowtimeBackend.Dtos.Admin;
using ShowtimeBackend.Dtos.Client;
using ShowtimeBackend.Services.ShowSession;

namespace ShowtimeBackend.Controllers.Admin;

[ApiController]
[Route("api/v1/admin")]
[Produces("application/json")]
public class AdminShowSessionController : ControllerBase
{
    private readonly IAdminShowSessionService _adminService;

    public AdminShowSessionController(IAdminShowSessionService adminService)
    {
        _adminService = adminService;
    }

    /// <summary>
    /// 为指定演出创建/排布场次
    /// </summary>
    [HttpPost("shows/{showId:long}/sessions")]
    [ProducesResponseType(typeof(ShowSessionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ShowSessionDto>> CreateSession(
        [FromRoute] long showId,
        [FromBody] CreateShowSessionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var createdSession = await _adminService.CreateSessionAsync(showId, request, cancellationToken);
            return CreatedAtAction(
                "GetOnSaleSessions",
                "ShowSession",
                new { showId = showId },
                createdSession);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 配置或覆盖更新场次票价策略
    /// </summary>
    [HttpPost("sessions/{sessionId:long}/pricing-strategies")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ConfigurePriceStrategies(
        [FromRoute] long sessionId,
        [FromBody] IEnumerable<CreatePriceStrategyRequest> requests,
        CancellationToken cancellationToken)
    {
        try
        {
            await _adminService.ConfigurePriceStrategiesAsync(sessionId, requests, cancellationToken);
            return Ok(new { message = "票价策略配置成功" });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 变更场次状态（如手动停售、恢复或下架）
    /// </summary>

    [HttpPut("sessions/{sessionId:long}/status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateSessionStatus(
        [FromRoute] long sessionId,
        [FromBody] UpdateSessionStatusRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _adminService.UpdateSessionStatusAsync(sessionId, request.Status, cancellationToken);
            return Ok(new { message = $"场次状态已更新为 {request.Status}" });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
