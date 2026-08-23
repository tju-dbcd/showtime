using ShowtimeBackend.Common.TicketSecurity;
using ShowtimeBackend.Entities.OrderTicket;

namespace ShowtimeBackend.Services.OrderTicket;

public sealed class TicketIssuanceService(ITicketTokenService ticketTokenService)
    : ITicketIssuanceService
{
    public OrderTicketResult<TicketIssuanceOutcome> Issue(
        Order order,
        TicketIssuanceContext context,
        string actor,
        DateTimeOffset operationTime)
    {
        if (!IsIssuableState(order.OrderStatus, context))
        {
            return Conflict(
                "TICKET_ORDER_NOT_ISSUABLE",
                "The order state does not allow ticket issuance in this context.");
        }

        if (!order.Payments.Any(payment => payment.PayStatus == "SUCCESS"))
        {
            return Conflict(
                "TICKET_SUCCESSFUL_PAYMENT_REQUIRED",
                "A successful payment is required before issuing tickets.");
        }

        if (order.Items.Count == 0)
        {
            return Conflict(
                "TICKET_ORDER_ITEMS_EMPTY",
                "The order has no items to issue.");
        }

        if (order.TicketCount != order.Items.Count ||
            order.Items.Any(item => item.ItemStatus != "NORMAL") ||
            order.Items.Any(item =>
                item.ETicket is not null &&
                (item.ETicket.OrderItemId != item.OrderItemId ||
                 item.ETicket.UserId != order.UserId ||
                 item.ETicket.TicketStatus != "UNUSED")))
        {
            return Conflict(
                "TICKET_DATA_INCONSISTENT",
                "The order items and existing tickets are inconsistent.");
        }

        var tokenTime = order.OrderStatus == "ISSUED" && order.IssueTime.HasValue
            ? new DateTimeOffset(
                DateTime.SpecifyKind(order.IssueTime.Value, DateTimeKind.Utc))
            : operationTime;
        var existingTicketCount = order.Items.Count(item => item.ETicket is not null);

        foreach (var item in order.Items.Where(item => item.ETicket is null))
        {
            var credential = ticketTokenService.Generate(tokenTime);
            item.ETicket = new ETicket
            {
                ETicketNo = credential.TicketNo,
                OrderItemId = item.OrderItemId,
                UserId = order.UserId,
                QrCode = credential.QrCode,
                AntiFakeCode = credential.AntiFakeCode,
                TicketStatus = "UNUSED",
                CreateBy = actor,
                UpdateBy = actor,
                OrderItem = item,
            };
        }

        order.OrderStatus = "ISSUED";
        order.IssueTime = tokenTime.UtcDateTime;
        order.UpdateBy = actor;

        var totalTicketCount = order.Items.Count(item => item.ETicket is not null);
        return OrderTicketResult<TicketIssuanceOutcome>.Success(
            new TicketIssuanceOutcome(
                totalTicketCount - existingTicketCount,
                existingTicketCount,
                totalTicketCount,
                order.IssueTime.Value));
    }

    private static bool IsIssuableState(
        string orderStatus,
        TicketIssuanceContext context) => context switch
    {
        TicketIssuanceContext.Payment => orderStatus == "PENDING_PAY",
        TicketIssuanceContext.Compensation => orderStatus is "PAID" or "ISSUED",
        _ => false,
    };

    private static OrderTicketResult<TicketIssuanceOutcome> Conflict(
        string code,
        string message) =>
        OrderTicketResult<TicketIssuanceOutcome>.Fail(
            OrderTicketFailure.Conflict,
            code,
            message);
}
