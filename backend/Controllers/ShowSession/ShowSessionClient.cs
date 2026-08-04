using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.ShowSessionDto;
using ShowtimeBackend.Services.ShowSession;

namespace ShowtimeBackend.Controllers.ShowSession.Client;

[ApiController]
[Route("api/client")]
[Produces("application/json")]
[AllowAnonymous]
public class ShowSessionClientController : ControllerBase
{
    private readonly IClientShowSessionService _sessionService;

    public ShowSessionClientController(IClientShowSessionService sessionService)
    {
        _sessionService = sessionService;
    }

    /// <summary>
    /// 获取指定演出的有效可售场次列表
    /// </summary>
    [HttpGet("shows/{showId:long}/sessions")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<ShowSessionDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<ShowSessionDto>>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<ShowSessionDto>>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<IEnumerable<ShowSessionDto>>>> GetOnSaleSessions(
        [FromRoute] long showId,
        CancellationToken cancellationToken)
    {
        if (showId <= 0)
        {
            return BadRequest(ApiResponse<IEnumerable<ShowSessionDto>>.Fail("INVALID_PARAM", "无效的演出 ID"));
        }

        try
        {
            var sessions = await _sessionService.GetOnSaleSessionsAsync(showId, cancellationToken);
            return Ok(ApiResponse<IEnumerable<ShowSessionDto>>.Ok(sessions, "获取场次列表成功"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<IEnumerable<ShowSessionDto>>.Fail("NOT_FOUND", ex.Message));
        }
    }

    /// <summary>
    /// 获取指定场次的区域票价策略列表
    /// </summary>
    [HttpGet("sessions/{sessionId:long}/pricing-strategies")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<PricingStrategyDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<PricingStrategyDto>>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<PricingStrategyDto>>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<IEnumerable<PricingStrategyDto>>>> GetPricingStrategies(
        [FromRoute] long sessionId,
        CancellationToken cancellationToken)
    {
        if (sessionId <= 0)
        {
            return BadRequest(ApiResponse<IEnumerable<PricingStrategyDto>>.Fail("INVALID_PARAM", "无效的场次 ID"));
        }

        try
        {
            var strategies = await _sessionService.GetPricingStrategiesAsync(sessionId, cancellationToken);
            return Ok(ApiResponse<IEnumerable<PricingStrategyDto>>.Ok(strategies, "获取票价策略成功"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<IEnumerable<PricingStrategyDto>>.Fail("NOT_FOUND", ex.Message));
        }
    }
}
