using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Services.OrderTicket;
using ShowtimeBackend.Services.UserPermission;

namespace ShowtimeBackend.Controllers.OrderTicket;

[ApiController]
[Authorize]
[Route("api/orders")]
[Tags("Orders")]
public sealed class OrdersController(
    IOrderService orderService,
    IOperationLogWriter operationLogWriter,
    TimeProvider timeProvider) : OrderTicketControllerBase
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
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromBody] CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUser(out var userId, out var actor))
        {
            return UnauthorizedResponse<OrderResponse>();
        }

        var startedAt = timeProvider.GetTimestamp();
        var result = await orderService.CreateAsync(
            userId,
            actor,
            idempotencyKey,
            request,
            cancellationToken);
        var costTime = Math.Max(
            0,
            (long)Math.Ceiling(timeProvider.GetElapsedTime(startedAt).TotalMilliseconds));
        await operationLogWriter.WriteBestEffortAsync(
            new OperationLogWriteRequest(
                Module: "ORDER",
                OperationType: "ORDER_CREATE",
                Succeeded: result.IsSuccess,
                UserId: userId,
                UserName: actor,
                CostTimeMilliseconds: costTime,
                RequestSummary: new
                {
                    request.SessionId,
                    TicketCount = request.Items.Count,
                },
                ResponseSummary: result.IsSuccess
                    ? new
                    {
                        ResultCode = "SUCCESS",
                        result.Value!.OrderId,
                        result.Value.OrderNo,
                    }
                    : new
                    {
                        ResultCode = result.ErrorCode ?? "ORDER_CREATE_FAILED",
                        OrderId = (long?)null,
                        OrderNo = (string?)null,
                    },
                ErrorMessage: result.IsSuccess ? null : result.ErrorCode),
            cancellationToken);
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
