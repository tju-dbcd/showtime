using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Services.OrderTicket;

namespace ShowtimeBackend.Controllers.OrderTicket;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/exchange-policies")]
[Tags("Admin Exchange Policies")]
public sealed class AdminExchangePoliciesController(
    IExchangePolicyAdminService service) : OrderTicketControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedExchangePolicyResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PagedExchangePolicyResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<PagedExchangePolicyResponse>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<PagedExchangePolicyResponse>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedExchangePolicyResponse>>> List(
        [FromQuery] ExchangePolicyListQuery query,
        CancellationToken cancellationToken)
    {
        var result = await service.ListAsync(query, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<PagedExchangePolicyResponse>.Ok(result.Value!, "Exchange policies retrieved."))
            : FailureResponse(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<ExchangePolicyResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<ExchangePolicyResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<ExchangePolicyResponse>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<ExchangePolicyResponse>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<ExchangePolicyResponse>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ExchangePolicyResponse>>> Create(
        [FromBody] SaveExchangePolicyRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUser(out _, out var actor))
        {
            return UnauthorizedResponse<ExchangePolicyResponse>();
        }

        var result = await service.CreateAsync(actor, request, cancellationToken);
        return result.IsSuccess
            ? StatusCode(
                StatusCodes.Status201Created,
                ApiResponse<ExchangePolicyResponse>.Ok(result.Value!, "Exchange policy created."))
            : FailureResponse(result);
    }

    [HttpPut("{policyId:long}")]
    [ProducesResponseType(typeof(ApiResponse<ExchangePolicyResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ExchangePolicyResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<ExchangePolicyResponse>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<ExchangePolicyResponse>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<ExchangePolicyResponse>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ExchangePolicyResponse>>> Update(
        long policyId,
        [FromBody] SaveExchangePolicyRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUser(out _, out var actor))
        {
            return UnauthorizedResponse<ExchangePolicyResponse>();
        }

        var result = await service.UpdateAsync(actor, policyId, request, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<ExchangePolicyResponse>.Ok(result.Value!, "Exchange policy updated."))
            : FailureResponse(result);
    }

    [HttpPatch("{policyId:long}/status")]
    [ProducesResponseType(typeof(ApiResponse<ExchangePolicyResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ExchangePolicyResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<ExchangePolicyResponse>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<ExchangePolicyResponse>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<ExchangePolicyResponse>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ExchangePolicyResponse>>> UpdateStatus(
        long policyId,
        [FromBody] UpdateExchangePolicyStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUser(out _, out var actor))
        {
            return UnauthorizedResponse<ExchangePolicyResponse>();
        }

        var result = await service.UpdateStatusAsync(actor, policyId, request, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<ExchangePolicyResponse>.Ok(result.Value!, "Exchange policy status updated."))
            : FailureResponse(result);
    }
}
