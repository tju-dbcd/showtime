using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Services.OrderTicket;

namespace ShowtimeBackend.Controllers.OrderTicket;

[ApiController]
[Authorize]
[Route("api/orders/{orderId:long}/tickets")]
[Tags("Tickets")]
public sealed class OrderTicketsController(ITicketQueryService ticketQueryService)
    : OrderTicketControllerBase
{
    [HttpGet]
    [ProducesResponseType(
        typeof(ApiResponse<IReadOnlyList<TicketResponse>>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ApiResponse<IReadOnlyList<TicketResponse>>),
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        typeof(ApiResponse<IReadOnlyList<TicketResponse>>),
        StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TicketResponse>>>> List(
        long orderId,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUser(out var userId, out _))
        {
            return UnauthorizedResponse<IReadOnlyList<TicketResponse>>();
        }

        var result = await ticketQueryService.ListForOwnerAsync(
            userId,
            orderId,
            cancellationToken);
        if (!result.IsSuccess)
        {
            return FailureResponse(result);
        }

        Response.Headers.CacheControl = "private, no-store";
        return Ok(ApiResponse<IReadOnlyList<TicketResponse>>.Ok(
            result.Value!,
            "Tickets retrieved."));
    }
}
