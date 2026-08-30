using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Services.OrderTicket;

namespace ShowtimeBackend.Controllers.OrderTicket;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/tickets")]
[Tags("Admin Tickets")]
public sealed class AdminTicketsController(ITicketRedemptionService service)
    : OrderTicketControllerBase
{
    [HttpPost("redeem")]
    [ProducesResponseType(typeof(ApiResponse<TicketRedemptionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<TicketRedemptionResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<TicketRedemptionResponse>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<TicketRedemptionResponse>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<TicketRedemptionResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<TicketRedemptionResponse>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<TicketRedemptionResponse>), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<TicketRedemptionResponse>>> Redeem(
        [FromBody] RedeemTicketRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUser(out _, out var actor))
        {
            return UnauthorizedResponse<TicketRedemptionResponse>();
        }

        var result = await service.RedeemAsync(actor, request, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<TicketRedemptionResponse>.Ok(
                result.Value!,
                "Ticket redeemed."))
            : FailureResponse(result);
    }
}
