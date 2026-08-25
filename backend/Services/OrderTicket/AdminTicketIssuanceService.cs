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
    ILogger<AdminTicketIssuanceService> logger,
    IOrderTicketAuditSink auditSink) : IAdminTicketIssuanceService
{
    public async Task<OrderTicketResult<TicketIssuanceResponse>> IssueAsync(
        string actor,
        long orderId,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var order = await LoadTrackedOrderAsync(orderId, cancellationToken);
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
                return Failed();
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
                await WriteAuditSafelyAsync(
                    new OrderTicketAuditEvent(
                        "ADMIN_TICKET_ISSUED",
                        order.OrderId,
                        actor,
                        issuanceResult.Value!.TotalTicketCount,
                        issuanceResult.Value.IssueTime),
                    cancellationToken);
                return OrderTicketResult<TicketIssuanceResponse>.Success(
                    ToResponse(order, issuanceResult.Value!));
            }
            catch (DbUpdateConcurrencyException exception)
            {
                logger.LogWarning(
                    exception,
                    "Compensation ticket issuance conflicted for order {OrderId}.",
                    orderId);
                return await RecoverCompleteResultAsync(orderId, cancellationToken);
            }
            catch (DbUpdateException exception)
            {
                var constraint = TicketConstraintClassifier.Classify(exception);
                if (constraint is TicketUniqueConstraint.TicketNumber or
                    TicketUniqueConstraint.QrCode or
                    TicketUniqueConstraint.AntiFakeCode && attempt == 0)
                {
                    logger.LogWarning(
                        exception,
                        "Generated ticket identifier collided for order {OrderId}; retrying once.",
                        orderId);
                    dbContext.ChangeTracker.Clear();
                    continue;
                }

                if (constraint == TicketUniqueConstraint.OrderItem)
                {
                    logger.LogWarning(
                        exception,
                        "Concurrent ticket creation detected for order {OrderId}.",
                        orderId);
                    return await RecoverCompleteResultAsync(orderId, cancellationToken);
                }

                logger.LogError(
                    exception,
                    "Ticket persistence failed for order {OrderId}.",
                    orderId);
                dbContext.ChangeTracker.Clear();
                return Failed();
            }
        }

        return Failed();
    }

    private Task<Order?> LoadTrackedOrderAsync(
        long orderId,
        CancellationToken cancellationToken) => dbContext.Set<Order>()
        .Include(item => item.Payments)
        .Include(item => item.Items)
            .ThenInclude(item => item.ETicket)
        .SingleOrDefaultAsync(item => item.OrderId == orderId, cancellationToken);

    private async Task<OrderTicketResult<TicketIssuanceResponse>> RecoverCompleteResultAsync(
        long orderId,
        CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        var order = await dbContext.Set<Order>()
            .AsNoTracking()
            .Include(item => item.Payments)
            .Include(item => item.Items)
                .ThenInclude(item => item.ETicket)
            .SingleOrDefaultAsync(item => item.OrderId == orderId, cancellationToken);
        if (order is not null && IsCompleteIssuedOrder(order))
        {
            var ticketCount = order.Items.Count;
            return OrderTicketResult<TicketIssuanceResponse>.Success(
                new TicketIssuanceResponse(
                    order.OrderId,
                    OrderStatus.ISSUED,
                    0,
                    ticketCount,
                    ticketCount,
                    order.IssueTime!.Value));
        }

        return OrderTicketResult<TicketIssuanceResponse>.Fail(
            OrderTicketFailure.Conflict,
            "TICKET_ISSUANCE_CONFLICT",
            "Ticket issuance conflicted with another request.");
    }

    private static bool IsCompleteIssuedOrder(Order order) =>
        order.OrderStatus == "ISSUED" &&
        order.IssueTime.HasValue &&
        order.Items.Count > 0 &&
        order.TicketCount == order.Items.Count &&
        order.Payments.Any(payment => payment.PayStatus == "SUCCESS") &&
        order.Items.All(item =>
            item.ItemStatus == "NORMAL" &&
            item.ETicket is not null &&
            item.ETicket.OrderItemId == item.OrderItemId &&
            item.ETicket.UserId == order.UserId &&
            item.ETicket.TicketStatus == "UNUSED");

    private async ValueTask WriteAuditSafelyAsync(
        OrderTicketAuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        try
        {
            await auditSink.WriteAsync(auditEvent, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Order-ticket audit sink failed for order {OrderId}.",
                auditEvent.OrderId);
        }
    }

    private static OrderTicketResult<TicketIssuanceResponse> Failed() =>
        OrderTicketResult<TicketIssuanceResponse>.Fail(
            OrderTicketFailure.Internal,
            "TICKET_ISSUANCE_FAILED",
            "Ticket issuance failed.");

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
