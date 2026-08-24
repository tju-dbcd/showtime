using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Services.OrderTicket;

namespace ShowtimeBackend.Controllers.OrderTicket;

[ApiController]
[Authorize]
[Route("api")]
[Tags("Refunds")]
public sealed class RefundsController(IRefundApplicationService service)
    : OrderTicketControllerBase
{
    [HttpPost("orders/{orderId:long}/refunds/quote")]
    [ProducesResponseType(typeof(ApiResponse<RefundQuoteResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<RefundQuoteResponse>>> Quote(
        long orderId,
        [FromBody] RefundQuoteRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUser(out var userId, out _))
        {
            return UnauthorizedResponse<RefundQuoteResponse>();
        }

        var result = await service.QuoteAsync(
            userId,
            orderId,
            request,
            cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<RefundQuoteResponse>.Ok(result.Value!, "Refund quoted."))
            : FailureResponse(result);
    }

    [HttpPost("orders/{orderId:long}/refunds")]
    [ProducesResponseType(typeof(ApiResponse<RefundResponse>), StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<RefundResponse>>> Create(
        long orderId,
        [FromBody] CreateRefundRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUser(out var userId, out var actor))
        {
            return UnauthorizedResponse<RefundResponse>();
        }

        var result = await service.CreateAsync(
            userId,
            actor,
            orderId,
            request,
            cancellationToken);
        return result.IsSuccess
            ? Created(
                $"/api/refunds/{result.Value!.RefundId}",
                ApiResponse<RefundResponse>.Ok(result.Value, "Refund requested."))
            : FailureResponse(result);
    }

    [HttpGet("orders/{orderId:long}/refunds")]
    [ProducesResponseType(typeof(ApiResponse<PagedRefundResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedRefundResponse>>> List(
        long orderId,
        [FromQuery] RefundListQuery query,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUser(out var userId, out _))
        {
            return UnauthorizedResponse<PagedRefundResponse>();
        }

        var result = await service.ListAsync(userId, orderId, query, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<PagedRefundResponse>.Ok(result.Value!, "Refunds retrieved."))
            : FailureResponse(result);
    }

    [HttpGet("refunds/{refundId:long}")]
    [ProducesResponseType(typeof(ApiResponse<RefundResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<RefundResponse>>> Get(
        long refundId,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUser(out var userId, out _))
        {
            return UnauthorizedResponse<RefundResponse>();
        }

        var result = await service.GetAsync(userId, refundId, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<RefundResponse>.Ok(result.Value!, "Refund retrieved."))
            : FailureResponse(result);
    }
}
