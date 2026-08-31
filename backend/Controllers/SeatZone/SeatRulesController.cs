using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.SeatZone;
using ShowtimeBackend.Data;
using ShowtimeBackend.Services.SeatZone;

namespace ShowtimeBackend.Controllers.SeatZone;

/// <summary>
/// 管理端选座规则及其生效范围维护接口（仅 Admin 角色）。
/// </summary>
[ApiController]
[Authorize(Roles = "Admin")]
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
        return result.IsSuccess ? Ok(ApiResponse<PagedResponse<SeatRuleResponse>>.Ok(result.Data!, "Seat rules retrieved.")) : ToFailure(result);
    }

    [HttpGet("seat-rules/{seatRuleId:long}")]
    public async Task<ActionResult<ApiResponse<SeatRuleResponse>>> GetRule(long seatRuleId, CancellationToken cancellationToken)
    {
        var result = await _service.GetRuleAsync(seatRuleId, cancellationToken);
        return result.IsSuccess ? Ok(ApiResponse<SeatRuleResponse>.Ok(result.Data!, "Seat rule retrieved.")) : ToFailure(result);
    }

    [HttpPost("seat-rules")]
    public async Task<ActionResult<ApiResponse<SeatRuleResponse>>> CreateRule([FromBody] SeatRuleRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CreateRuleAsync(request, cancellationToken);
        if (!result.IsSuccess) return ToFailure(result);
        return CreatedAtAction(nameof(GetRule), new { seatRuleId = result.Data!.SeatRuleId }, ApiResponse<SeatRuleResponse>.Ok(result.Data, "Seat rule created."));
    }

    [HttpPut("seat-rules/{seatRuleId:long}")]
    public async Task<ActionResult<ApiResponse<SeatRuleResponse>>> UpdateRule(long seatRuleId, [FromBody] SeatRuleRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.UpdateRuleAsync(seatRuleId, request, cancellationToken);
        return result.IsSuccess ? Ok(ApiResponse<SeatRuleResponse>.Ok(result.Data!, "Seat rule updated.")) : ToFailure(result);
    }

    [HttpDelete("seat-rules/{seatRuleId:long}")]
    public async Task<IActionResult> DeleteRule(long seatRuleId, CancellationToken cancellationToken)
    {
        var result = await _service.DeleteRuleAsync(seatRuleId, cancellationToken);
        return result.IsSuccess ? NoContent() : ToFailure(result);
    }

    [HttpGet("seat-rules/{seatRuleId:long}/scopes")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SeatRuleScopeResponse>>>> ListScopes(long seatRuleId, CancellationToken cancellationToken)
    {
        var result = await _service.ListScopesAsync(seatRuleId, cancellationToken);
        return result.IsSuccess ? Ok(ApiResponse<IReadOnlyList<SeatRuleScopeResponse>>.Ok(result.Data!, "Rule scopes retrieved.")) : ToFailure(result);
    }

    [HttpPost("seat-rules/{seatRuleId:long}/scopes")]
    public async Task<ActionResult<ApiResponse<SeatRuleScopeResponse>>> CreateScope(long seatRuleId, [FromBody] SeatRuleScopeRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CreateScopeAsync(seatRuleId, request, cancellationToken);
        if (!result.IsSuccess) return ToFailure(result);
        return CreatedAtAction(nameof(ListScopes), new { seatRuleId }, ApiResponse<SeatRuleScopeResponse>.Ok(result.Data!, "Rule scope created."));
    }

    [HttpDelete("seat-rule-scopes/{ruleScopeId:long}")]
    public async Task<IActionResult> DeleteScope(long ruleScopeId, CancellationToken cancellationToken)
    {
        var result = await _service.DeleteScopeAsync(ruleScopeId, cancellationToken);
        return result.IsSuccess ? NoContent() : ToFailure(result);
    }

    private ActionResult ToFailure<T>(ServiceResult<T> result) => StatusCode(
        result.StatusCode ?? StatusCodes.Status500InternalServerError,
        ApiResponse<T>.Fail(result.Title!, result.Detail ?? result.Title!));
}
