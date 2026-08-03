using Microsoft.AspNetCore.Mvc;
using ShowtimeBackend.Dtos.Client;
using ShowtimeBackend.Services.ShowSession;

namespace ShowtimeBackend.Controllers.Client;

[ApiController]
[Route("api/v1")]
[Produces("application/json")]
public class ShowSessionController : ControllerBase
{
    private readonly IClientShowSessionService _sessionService;

    //获取一个具体的操控对象用于执行后续操作
    public ShowSessionController(IClientShowSessionService sessionService)
    {
        _sessionService = sessionService;
    }

    /// <summary>
    /// 获取指定演出的有效可售场次列表
    /// </summary>
    /// <remarks>
    /// 用于 C 端演出详情页。底层基于 (ShowId, SessionStatus, StartTime) 复合索引进行性能优化。
    /// </remarks>
    /// <param name="showId">演出主键 ID</param>
    /// <param name="cancellationToken">异步取消令牌</param>
    [HttpGet("shows/{showId:long}/sessions")]
    [ProducesResponseType(typeof(IEnumerable<ShowSessionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IEnumerable<ShowSessionDto>>> GetOnSaleSessions(
        [FromRoute] long showId,
        CancellationToken cancellationToken)
    {
        if (showId <= 0)
        {
            return BadRequest(new { message = "无效的演出 ID" });
        }

        var sessions = await _sessionService.GetOnSaleSessionsAsync(showId, cancellationToken);
        return Ok(sessions);
    }

    /// <summary>
    /// 获取指定场次的区域票价策略列表
    /// </summary>
    /// <remarks>
    /// 用于 C 端选座/选票页面加载价格面板。
    /// </remarks>
    /// <param name="sessionId">场次主键 ID</param>
    /// <param name="cancellationToken">异步取消令牌</param>
    [HttpGet("sessions/{sessionId:long}/pricing-strategies")]
    [ProducesResponseType(typeof(IEnumerable<PricingStrategyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IEnumerable<PricingStrategyDto>>> GetPricingStrategies(
        [FromRoute] long sessionId,
        CancellationToken cancellationToken)
    {
        if (sessionId <= 0)
        {
            return BadRequest(new { message = "无效的场次 ID" });
        }

        var strategies = await _sessionService.GetPricingStrategiesAsync(sessionId, cancellationToken);
        return Ok(strategies);
    }
}
