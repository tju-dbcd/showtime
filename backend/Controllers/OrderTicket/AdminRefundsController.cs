using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Services.OrderTicket;

namespace ShowtimeBackend.Controllers.OrderTicket;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/refunds")]
[Tags("Admin Refunds")]
public sealed class AdminRefundsController(IRefundReviewService service)
    : OrderTicketControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedRefundResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PagedRefundResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<PagedRefundResponse>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<PagedRefundResponse>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedRefundResponse>>> List(
        [FromQuery] AdminRefundListQuery query,
        CancellationToken cancellationToken)
    {
        if (HasNumericStatusQueryValue())
        {
            return BadRequest(
                ApiResponse<PagedRefundResponse>.Fail(
                    "VALIDATION_FAILED",
                    "ApproveStatus and RefundStatus must use string enum values."));
        }

        var result = await service.ListAsync(query, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<PagedRefundResponse>.Ok(result.Value!, "Refunds retrieved."))
            : FailureResponse(result);
    }

    [HttpGet("{refundId:long}")]
    [ProducesResponseType(typeof(ApiResponse<RefundResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<RefundResponse>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<RefundResponse>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<RefundResponse>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<RefundResponse>>> Get(
        long refundId,
        CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(refundId, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<RefundResponse>.Ok(result.Value!, "Refund retrieved."))
            : FailureResponse(result);
    }

    [HttpPost("{refundId:long}/approve")]
    [ProducesResponseType(typeof(ApiResponse<RefundResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<RefundResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<RefundResponse>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<RefundResponse>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<RefundResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<RefundResponse>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<RefundResponse>), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<RefundResponse>>> Approve(
        long refundId,
        [FromBody] ApproveRefundRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUser(out _, out var actor))
        {
            return UnauthorizedResponse<RefundResponse>();
        }

        var result = await service.ApproveAsync(
            actor,
            refundId,
            request,
            cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<RefundResponse>.Ok(result.Value!, "Refund approved."))
            : FailureResponse(result);
    }

    [HttpPost("{refundId:long}/reject")]
    [ProducesResponseType(typeof(ApiResponse<RefundResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<RefundResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<RefundResponse>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<RefundResponse>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<RefundResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<RefundResponse>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<RefundResponse>), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<RefundResponse>>> Reject(
        long refundId,
        [FromBody] RejectRefundRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUser(out _, out var actor))
        {
            return UnauthorizedResponse<RefundResponse>();
        }

        var result = await service.RejectAsync(
            actor,
            refundId,
            request,
            cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<RefundResponse>.Ok(result.Value!, "Refund rejected."))
            : FailureResponse(result);
    }

    private bool HasNumericStatusQueryValue()
    {
        foreach (var (name, values) in Request.Query)
        {
            if (!name.Equals("approveStatus", StringComparison.OrdinalIgnoreCase) &&
                !name.Equals("refundStatus", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var value in values)
            {
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
