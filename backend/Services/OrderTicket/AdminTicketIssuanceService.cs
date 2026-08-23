using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Common;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.OrderTicket;

namespace ShowtimeBackend.Services.OrderTicket;

public sealed class AdminTicketIssuanceService(
    AppDbContext dbContext,
    TimeProvider timeProvider,
    ITicketIssuanceService ticketIssuanceService,
    ILogger<AdminTicketIssuanceService> logger) : IAdminTicketIssuanceService
{
    public async Task<OrderTicketResult<TicketIssuanceResponse>> IssueAsync(
        string actor,
        long orderId,
        CancellationToken cancellationToken)
    {
        var order = await dbContext.Set<Order>()
            .Include(item => item.Payments)
            .Include(item => item.Items)
                .ThenInclude(item => item.ETicket)
            .SingleOrDefaultAsync(item => item.OrderId == orderId, cancellationToken);
        if (order is null)
        {
            return OrderTicketResult<TicketIssuanceResponse>.Fail(
                OrderTicketFailure.NotFound,
                "TICKET_ORDER_NOT_FOUND",
                "The order does not exist.");
        }

        OrderTicketResult<TicketIssuanceOutcome> issuanceResult;
        try
        {
            issuanceResult = ticketIssuanceService.Issue(
                order,
                TicketIssuanceContext.Compensation,
                actor,
                timeProvider.GetUtcNow());
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Compensation ticket issuance failed before saving order {OrderId}.",
                orderId);
            dbContext.ChangeTracker.Clear();
            return OrderTicketResult<TicketIssuanceResponse>.Fail(
                OrderTicketFailure.Internal,
                "TICKET_ISSUANCE_FAILED",
                "Ticket issuance failed.");
        }

        if (!issuanceResult.IsSuccess)
        {
            dbContext.ChangeTracker.Clear();
            return OrderTicketResult<TicketIssuanceResponse>.Fail(
                issuanceResult.Failure,
                issuanceResult.ErrorCode!,
                issuanceResult.Message!);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            logger.LogWarning(
                exception,
                "Compensation ticket issuance conflicted for order {OrderId}.",
                orderId);
            dbContext.ChangeTracker.Clear();
            return OrderTicketResult<TicketIssuanceResponse>.Fail(
                OrderTicketFailure.Conflict,
                "TICKET_ISSUANCE_CONFLICT",
                "Ticket issuance conflicted with another request.");
        }

        return OrderTicketResult<TicketIssuanceResponse>.Success(
            ToResponse(order, issuanceResult.Value!));
    }

    private static TicketIssuanceResponse ToResponse(
        Order order,
        TicketIssuanceOutcome outcome) => new(
            order.OrderId,
            order.OrderStatus.ToEnum<OrderStatus>(),
            outcome.CreatedTicketCount,
            outcome.ExistingTicketCount,
            outcome.TotalTicketCount,
            outcome.IssueTime);
}
