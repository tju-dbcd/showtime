using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Services.OrderTicket;

namespace ShowtimeBackend.Controllers.OrderTicket;

[ApiController]
[Authorize]
[Route("api")]
[Tags("Exchanges")]
public sealed class ExchangesController(
    IExchangeApplicationService service,
    IExchangePaymentService paymentService)
    : OrderTicketControllerBase
{
    [HttpPost("orders/{orderId:long}/exchanges/quote")]
    [ProducesResponseType(typeof(ApiResponse<ExchangeQuoteResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ExchangeQuoteResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<ExchangeQuoteResponse>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<ExchangeQuoteResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<ExchangeQuoteResponse>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<ExchangeQuoteResponse>>> Quote(
        long orderId,
        [FromBody] ExchangeQuoteRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUser(out var userId, out _))
        {
            return UnauthorizedResponse<ExchangeQuoteResponse>();
        }

        var result = await service.QuoteAsync(userId, orderId, request, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<ExchangeQuoteResponse>.Ok(result.Value!, "Exchange quoted."))
            : FailureResponse(result);
    }

    [HttpPost("orders/{orderId:long}/exchanges")]
    [ProducesResponseType(typeof(ApiResponse<ExchangeResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<ExchangeResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<ExchangeResponse>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<ExchangeResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<ExchangeResponse>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<ExchangeResponse>>> Create(
        long orderId, [FromBody] CreateExchangeRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUser(out var userId, out var actor))
            return UnauthorizedResponse<ExchangeResponse>();
        var result = await service.CreateAsync(userId, actor, orderId, request, cancellationToken);
        return result.IsSuccess
            ? Created($"/api/exchanges/{result.Value!.ExchangeId}",
                ApiResponse<ExchangeResponse>.Ok(result.Value, "Exchange requested."))
            : FailureResponse(result);
    }

    [HttpGet("orders/{orderId:long}/exchanges")]
    [ProducesResponseType(typeof(ApiResponse<PagedExchangeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PagedExchangeResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<PagedExchangeResponse>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<PagedExchangeResponse>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<PagedExchangeResponse>>> List(
        long orderId, [FromQuery] ExchangeListQuery query, CancellationToken cancellationToken)
    {
        if (HasInvalidStatusQueryValue())
            return BadRequest(ApiResponse<PagedExchangeResponse>.Fail(
                "VALIDATION_FAILED", "ApproveStatus and ExchangeStatus must use string enum values."));
        if (!TryGetCurrentUser(out var userId, out _))
            return UnauthorizedResponse<PagedExchangeResponse>();
        var result = await service.ListAsync(userId, orderId, query, cancellationToken);
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

    [HttpGet("exchanges/{exchangeId:long}")]
    [ProducesResponseType(typeof(ApiResponse<ExchangeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ExchangeResponse>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<ExchangeResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<ExchangeResponse>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<ExchangeResponse>>> Get(
        long exchangeId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUser(out var userId, out _))
            return UnauthorizedResponse<ExchangeResponse>();
        var result = await service.GetAsync(userId, exchangeId, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<ExchangeResponse>.Ok(result.Value!, "Exchange retrieved."))
            : FailureResponse(result);
    }

    [HttpPost("exchanges/{exchangeId:long}/pay")]
    [ProducesResponseType(typeof(ApiResponse<ExchangePaymentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ExchangePaymentResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<ExchangePaymentResponse>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<ExchangePaymentResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<ExchangePaymentResponse>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<ExchangePaymentResponse>>> Pay(
        long exchangeId, [FromBody] ExchangePaymentRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUser(out var userId, out var actor))
            return UnauthorizedResponse<ExchangePaymentResponse>();
        var result = await paymentService.PayAsync(userId, actor, exchangeId, request, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<ExchangePaymentResponse>.Ok(result.Value!, "Exchange payment processed."))
            : FailureResponse(result);
    }
}
