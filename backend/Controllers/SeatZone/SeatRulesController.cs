using Microsoft.AspNetCore.Mvc;
using ShowtimeBackend.DTOs;
using ShowtimeBackend.DTOs.SeatZone;
using ShowtimeBackend.Data;
using ShowtimeBackend.Services.SeatZone;

namespace ShowtimeBackend.Controllers.SeatZone;

/// <summary>
/// 管理端选座规则及其生效范围维护接口。
/// </summary>
[ApiController]
[Route("api/admin")]
[Tags("Seat Zone Administration - Seat Rules")]
public sealed class SeatRulesController : ControllerBase
{
    private readonly SeatRuleAdminService _service;

    public SeatRulesController(AppDbContext db) => _service = new SeatRuleAdminService(db);

    [HttpGet("seat-rules")]
    public async Task<ActionResult<ApiResponse<PagedResponse<SeatRuleResponse>>>> ListRules([FromQuery] SeatRuleListQuery query, CancellationToken cancellationToken)
    {
        var result = await _service.ListRulesAsync(query, cancellationToken);
        return result.IsSuccess ? Ok(new ApiResponse<PagedResponse<SeatRuleResponse>>(result.Data!)) : ToProblem(result);
    }

    [HttpGet("seat-rules/{seatRuleId:long}")]
    public async Task<ActionResult<ApiResponse<SeatRuleResponse>>> GetRule(long seatRuleId, CancellationToken cancellationToken)
    {
        var result = await _service.GetRuleAsync(seatRuleId, cancellationToken);
        return result.IsSuccess ? Ok(new ApiResponse<SeatRuleResponse>(result.Data!)) : ToProblem(result);
    }

    [HttpPost("seat-rules")]
    public async Task<ActionResult<ApiResponse<SeatRuleResponse>>> CreateRule([FromBody] SeatRuleRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CreateRuleAsync(request, cancellationToken);
        if (!result.IsSuccess) return ToProblem(result);
        return CreatedAtAction(nameof(GetRule), new { seatRuleId = result.Data!.SeatRuleId }, new ApiResponse<SeatRuleResponse>(result.Data));
    }

    [HttpPut("seat-rules/{seatRuleId:long}")]
    public async Task<ActionResult<ApiResponse<SeatRuleResponse>>> UpdateRule(long seatRuleId, [FromBody] SeatRuleRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.UpdateRuleAsync(seatRuleId, request, cancellationToken);
        return result.IsSuccess ? Ok(new ApiResponse<SeatRuleResponse>(result.Data!)) : ToProblem(result);
    }

    [HttpDelete("seat-rules/{seatRuleId:long}")]
    public async Task<IActionResult> DeleteRule(long seatRuleId, CancellationToken cancellationToken)
    {
        var result = await _service.DeleteRuleAsync(seatRuleId, cancellationToken);
        return result.IsSuccess ? NoContent() : ToProblem(result);
    }

    [HttpGet("seat-rules/{seatRuleId:long}/scopes")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SeatRuleScopeResponse>>>> ListScopes(long seatRuleId, CancellationToken cancellationToken)
    {
        var result = await _service.ListScopesAsync(seatRuleId, cancellationToken);
        return result.IsSuccess ? Ok(new ApiResponse<IReadOnlyList<SeatRuleScopeResponse>>(result.Data!)) : ToProblem(result);
    }

    [HttpPost("seat-rules/{seatRuleId:long}/scopes")]
    public async Task<ActionResult<ApiResponse<SeatRuleScopeResponse>>> CreateScope(long seatRuleId, [FromBody] SeatRuleScopeRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CreateScopeAsync(seatRuleId, request, cancellationToken);
        if (!result.IsSuccess) return ToProblem(result);
        return CreatedAtAction(nameof(ListScopes), new { seatRuleId }, new ApiResponse<SeatRuleScopeResponse>(result.Data!));
    }

    [HttpDelete("seat-rule-scopes/{ruleScopeId:long}")]
    public async Task<IActionResult> DeleteScope(long ruleScopeId, CancellationToken cancellationToken)
    {
        var result = await _service.DeleteScopeAsync(ruleScopeId, cancellationToken);
        return result.IsSuccess ? NoContent() : ToProblem(result);
    }

    private ActionResult ToProblem<T>(ServiceResult<T> result) => Problem(statusCode: result.StatusCode, title: result.Title, detail: result.Detail);
}
