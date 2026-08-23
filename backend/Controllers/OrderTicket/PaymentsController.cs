using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Services.OrderTicket;

namespace ShowtimeBackend.Controllers.OrderTicket;

[ApiController]
[Authorize]
[Route("api/orders/{orderId:long}/payments")]
[Tags("Payments")]
public sealed class PaymentsController(IPaymentService paymentService) : OrderTicketControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PaymentResponse>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PaymentResponse>>>> List(
        long orderId,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUser(out var userId, out _))
        {
            return UnauthorizedResponse<IReadOnlyList<PaymentResponse>>();
        }

        var result = await paymentService.ListAsync(userId, orderId, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<IReadOnlyList<PaymentResponse>>.Ok(result.Value!, "Payments retrieved."))
            : FailureResponse(result);
    }

    [HttpPost("mock")]
    [ProducesResponseType(typeof(ApiResponse<PaymentProcessResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PaymentProcessResponse>>> MockPay(
        long orderId,
        [FromBody] MockPaymentRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUser(out var userId, out var actor))
        {
            return UnauthorizedResponse<PaymentProcessResponse>();
        }

        var result = await paymentService.PayAsync(userId, actor, orderId, request, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<PaymentProcessResponse>.Ok(result.Value!, "Mock payment processed."))
            : FailureResponse(result);
    }
}
