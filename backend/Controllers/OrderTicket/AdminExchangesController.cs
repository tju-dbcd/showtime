using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Services.OrderTicket;

namespace ShowtimeBackend.Controllers.OrderTicket;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/exchanges")]
[Tags("Admin Exchanges")]
public sealed class AdminExchangesController(IExchangeReviewService service)
    : OrderTicketControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedExchangeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PagedExchangeResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<PagedExchangeResponse>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<PagedExchangeResponse>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedExchangeResponse>>> List(
        [FromQuery] AdminExchangeListQuery query, CancellationToken cancellationToken)
    {
        if (HasInvalidStatusQueryValue())
            return BadRequest(ApiResponse<PagedExchangeResponse>.Fail(
                "VALIDATION_FAILED", "ApproveStatus and ExchangeStatus must use string enum values."));
        var result = await service.ListAsync(query, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<PagedExchangeResponse>.Ok(result.Value!, "Exchanges retrieved."))
            : FailureResponse(result);
    }

    private bool HasInvalidStatusQueryValue()
    {
        foreach (var (name, values) in Request.Query)
        {
            if (!name.Equals("approveStatus", StringComparison.OrdinalIgnoreCase) &&
                !name.Equals("exchangeStatus", StringComparison.OrdinalIgnoreCase)) continue;
            if (values.Any(value => string.IsNullOrWhiteSpace(value) ||
                    int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
                return true;
        }
        return false;
    }

    [HttpGet("{exchangeId:long}")]
    [ProducesResponseType(typeof(ApiResponse<ExchangeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ExchangeResponse>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<ExchangeResponse>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<ExchangeResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<ExchangeResponse>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<ExchangeResponse>>> Get(
        long exchangeId, CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(exchangeId, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<ExchangeResponse>.Ok(result.Value!, "Exchange retrieved."))
            : FailureResponse(result);
    }

    [HttpPost("{exchangeId:long}/reject")]
    [ProducesResponseType(typeof(ApiResponse<ExchangeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ExchangeResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<ExchangeResponse>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<ExchangeResponse>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<ExchangeResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<ExchangeResponse>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<ExchangeResponse>>> Reject(
        long exchangeId, [FromBody] RejectExchangeRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUser(out _, out var actor))
            return UnauthorizedResponse<ExchangeResponse>();
        var result = await service.RejectAsync(actor, exchangeId, request, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<ExchangeResponse>.Ok(result.Value!, "Exchange rejected."))
            : FailureResponse(result);
    }

    [HttpPost("{exchangeId:long}/approve")]
    [ProducesResponseType(typeof(ApiResponse<ExchangeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ExchangeResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<ExchangeResponse>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<ExchangeResponse>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<ExchangeResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<ExchangeResponse>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<ExchangeResponse>>> Approve(
        long exchangeId, [FromBody] ApproveExchangeRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUser(out _, out var actor))
            return UnauthorizedResponse<ExchangeResponse>();
        var result = await service.ApproveAsync(actor, exchangeId, request, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<ExchangeResponse>.Ok(result.Value!, "Exchange approved."))
            : FailureResponse(result);
    }
}
