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
