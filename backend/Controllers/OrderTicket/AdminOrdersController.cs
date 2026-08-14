using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Services.OrderTicket;

namespace ShowtimeBackend.Controllers.OrderTicket;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/orders")]
[Tags("Admin Orders")]
public sealed class AdminOrdersController(IOrderService orderService) : OrderTicketControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedAdminOrderResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PagedAdminOrderResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<PagedAdminOrderResponse>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<PagedAdminOrderResponse>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedAdminOrderResponse>>> List(
        [FromQuery] AdminOrderListQuery query,
        CancellationToken cancellationToken)
    {
        var result = await orderService.ListAdminAsync(query, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<PagedAdminOrderResponse>.Ok(result.Value!, "Orders retrieved."))
            : FailureResponse(result);
    }

    [HttpGet("{orderId:long}")]
    [ProducesResponseType(typeof(ApiResponse<OrderResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<OrderResponse>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<OrderResponse>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<OrderResponse>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<OrderResponse>>> Get(
        long orderId,
        CancellationToken cancellationToken)
    {
        var result = await orderService.GetAdminAsync(orderId, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<OrderResponse>.Ok(result.Value!, "Order retrieved."))
            : FailureResponse(result);
    }

    [HttpPatch("{orderId:long}/cancel")]
    [ProducesResponseType(typeof(ApiResponse<OrderResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<OrderResponse>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<OrderResponse>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<OrderResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<OrderResponse>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<OrderResponse>>> Cancel(
        long orderId,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUser(out _, out var actor))
        {
            return UnauthorizedResponse<OrderResponse>();
        }

        var result = await orderService.CancelAdminAsync(actor, orderId, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<OrderResponse>.Ok(result.Value!, "Order cancelled."))
            : FailureResponse(result);
    }
}
