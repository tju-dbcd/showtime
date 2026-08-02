using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Services.OrderTicket;

namespace ShowtimeBackend.Controllers.OrderTicket;

[ApiController]
[Authorize]
[Route("api/orders")]
[Tags("Orders")]
public sealed class OrdersController(IOrderService orderService) : OrderTicketControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedOrderResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedOrderResponse>>> List(
        [FromQuery] OrderListQuery query,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUser(out var userId, out _))
        {
            return UnauthorizedResponse<PagedOrderResponse>();
        }

        var result = await orderService.ListAsync(userId, query, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<PagedOrderResponse>.Ok(result.Value!, "Orders retrieved."))
            : FailureResponse(result);
    }

    [HttpGet("{orderId:long}")]
    [ProducesResponseType(typeof(ApiResponse<OrderResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<OrderResponse>>> Get(
        long orderId,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUser(out var userId, out _))
        {
            return UnauthorizedResponse<OrderResponse>();
        }

        var result = await orderService.GetAsync(userId, orderId, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<OrderResponse>.Ok(result.Value!, "Order retrieved."))
            : FailureResponse(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<OrderResponse>), StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<OrderResponse>>> Create(
        [FromBody] CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUser(out var userId, out var actor))
        {
            return UnauthorizedResponse<OrderResponse>();
        }

        var result = await orderService.CreateAsync(userId, actor, request, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(
                nameof(Get),
                new { orderId = result.Value!.OrderId },
                ApiResponse<OrderResponse>.Ok(result.Value, "Order created."))
            : FailureResponse(result);
    }

    [HttpPatch("{orderId:long}/cancel")]
    [ProducesResponseType(typeof(ApiResponse<OrderResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<OrderResponse>>> Cancel(
        long orderId,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUser(out var userId, out var actor))
        {
            return UnauthorizedResponse<OrderResponse>();
        }

        var result = await orderService.CancelAsync(userId, actor, orderId, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<OrderResponse>.Ok(result.Value!, "Order cancelled."))
            : FailureResponse(result);
    }
}
