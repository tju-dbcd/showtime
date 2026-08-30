using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Services.OrderTicket;

namespace ShowtimeBackend.Controllers.OrderTicket;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/refund-policies")]
[Tags("Admin Refund Policies")]
public sealed class AdminRefundPoliciesController(
    IRefundPolicyAdminService service) : OrderTicketControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedRefundPolicyResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PagedRefundPolicyResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<PagedRefundPolicyResponse>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<PagedRefundPolicyResponse>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedRefundPolicyResponse>>> List(
        [FromQuery] RefundPolicyListQuery query,
        CancellationToken cancellationToken)
    {
        var result = await service.ListAsync(query, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<PagedRefundPolicyResponse>.Ok(result.Value!, "Refund policies retrieved."))
            : FailureResponse(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<RefundPolicyResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<RefundPolicyResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<RefundPolicyResponse>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<RefundPolicyResponse>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<RefundPolicyResponse>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<RefundPolicyResponse>>> Create(
        [FromBody] SaveRefundPolicyRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUser(out _, out var actor))
        {
            return UnauthorizedResponse<RefundPolicyResponse>();
        }

        var result = await service.CreateAsync(actor, request, cancellationToken);
        return result.IsSuccess
            ? StatusCode(
                StatusCodes.Status201Created,
                ApiResponse<RefundPolicyResponse>.Ok(result.Value!, "Refund policy created."))
            : FailureResponse(result);
    }

    [HttpPut("{policyId:long}")]
    [ProducesResponseType(typeof(ApiResponse<RefundPolicyResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<RefundPolicyResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<RefundPolicyResponse>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<RefundPolicyResponse>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<RefundPolicyResponse>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<RefundPolicyResponse>>> Update(
        long policyId,
        [FromBody] SaveRefundPolicyRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUser(out _, out var actor))
        {
            return UnauthorizedResponse<RefundPolicyResponse>();
        }

        var result = await service.UpdateAsync(actor, policyId, request, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<RefundPolicyResponse>.Ok(result.Value!, "Refund policy updated."))
            : FailureResponse(result);
    }

    [HttpPatch("{policyId:long}/status")]
    [ProducesResponseType(typeof(ApiResponse<RefundPolicyResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<RefundPolicyResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<RefundPolicyResponse>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<RefundPolicyResponse>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<RefundPolicyResponse>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<RefundPolicyResponse>>> UpdateStatus(
        long policyId,
        [FromBody] UpdateRefundPolicyStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUser(out _, out var actor))
        {
            return UnauthorizedResponse<RefundPolicyResponse>();
        }

        var result = await service.UpdateStatusAsync(actor, policyId, request, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<RefundPolicyResponse>.Ok(result.Value!, "Refund policy status updated."))
            : FailureResponse(result);
    }
}
