using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Linq.Expressions;
using ShowtimeBackend.Common;
using ShowtimeBackend.Common.TicketSecurity;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.OrderTicket;
using ShowSessionEntity = ShowtimeBackend.Entities.ShowSession.ShowSession;

namespace ShowtimeBackend.Services.OrderTicket;

public sealed class TicketRedemptionService(
    AppDbContext dbContext,
    ITicketTokenService ticketTokenService,
    TimeProvider timeProvider,
    IOptions<TicketRedemptionOptions> options,
    ILogger<TicketRedemptionService> logger,
    IOrderTicketAuditSink auditSink) : ITicketRedemptionService
{
    private readonly TicketRedemptionOptions redemptionOptions = options.Value;

    public async Task<OrderTicketResult<TicketRedemptionResponse>> RedeemAsync(
        string actor,
        RedeemTicketRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.QrCode) ||
            request.QrCode.Length > 255 ||
            !ticketTokenService.TryValidate(request.QrCode, out var payload) ||
            payload is null)
        {
            return Invalid(
                "TICKET_QR_INVALID",
                "The ticket QR code is invalid.");
        }

        var checkDevice = request.CheckDevice?.Trim();
        if (string.IsNullOrEmpty(checkDevice) || checkDevice.Length > 100)
        {
            return Invalid(
                "TICKET_DEVICE_INVALID",
                "Check device must contain between 1 and 100 characters.");
        }

        var operationTime = TruncateToMicroseconds(
            timeProvider.GetUtcNow().UtcDateTime);
        var snapshot = await LoadSnapshotAsync(
            payload.TicketNo,
            request.QrCode,
            cancellationToken);
        if (snapshot is null)
        {
            return NotFound(
                "TICKET_NOT_FOUND",
                "The ticket does not exist.");
        }

        var eligibility = EvaluateEligibility(snapshot, operationTime);
        if (eligibility is not null)
        {
            return eligibility;
        }

        var latestAllowedStart = operationTime.AddMinutes(
            redemptionOptions.OpenBeforeMinutes);
        var earliestAllowedEnd = operationTime.AddMinutes(
            -redemptionOptions.CloseAfterMinutes);
        var affectedRows = await dbContext.Set<ETicket>()
            .Where(ticket =>
                ticket.ETicketId == snapshot.ETicketId &&
                ticket.QrCode == request.QrCode &&
                ticket.TicketStatus == "UNUSED" &&
                ticket.CheckTime == null &&
                ticket.CheckDevice == null &&
                ticket.CheckBy == null &&
                dbContext.Set<OrderItem>().Any(item =>
                    item.OrderItemId == ticket.OrderItemId &&
                    item.ItemStatus == "NORMAL" &&
                    dbContext.Set<Order>().Any(order =>
                        order.OrderId == item.OrderId &&
                        (order.OrderStatus == "ISSUED" ||
                         order.OrderStatus == "PART_REFUND") &&
                        dbContext.Set<ShowSessionEntity>().Any(session =>
                            session.SessionId == order.SessionId &&
                            session.StartTime <= latestAllowedStart &&
                            session.EndTime >= earliestAllowedEnd))))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(ticket => ticket.TicketStatus, "USED")
                    .SetProperty(ticket => ticket.CheckTime, operationTime)
                    .SetProperty(ticket => ticket.CheckDevice, checkDevice)
                    .SetProperty(ticket => ticket.CheckBy, actor)
                    .SetProperty(ticket => ticket.UpdateBy, actor),
                cancellationToken);

        dbContext.ChangeTracker.Clear();
        var persisted = await LoadSnapshotByIdAsync(
            snapshot.ETicketId,
            cancellationToken);
        if (affectedRows == 0)
        {
            return persisted is null
                ? Conflict(
                    "TICKET_REDEMPTION_CONFLICT",
                    "The ticket changed while it was being redeemed.")
                : EvaluateEligibility(persisted, operationTime) ?? Conflict(
                    "TICKET_REDEMPTION_CONFLICT",
                    "The ticket changed while it was being redeemed.");
        }

        if (persisted?.CheckTime is null ||
            persisted.CheckDevice is null ||
            persisted.CheckBy is null ||
            persisted.TicketStatus != "USED")
        {
            return OrderTicketResult<TicketRedemptionResponse>.Fail(
                OrderTicketFailure.Internal,
                "TICKET_REDEMPTION_FAILED",
                "The ticket was updated but could not be reloaded.");
        }

        var response = ToResponse(persisted);
        await WriteAuditSafelyAsync(
            new OrderTicketAuditEvent(
                "TICKET_REDEEMED",
                persisted.OrderId,
                actor,
                1,
                response.CheckTime,
                Metadata: new Dictionary<string, string>
                {
                    ["ETicketId"] = persisted.ETicketId.ToString(),
                    ["ETicketNo"] = persisted.ETicketNo,
                    ["SessionId"] = persisted.SessionId.ToString(),
                    ["CheckDevice"] = persisted.CheckDevice,
                }),
            cancellationToken);
        return OrderTicketResult<TicketRedemptionResponse>.Success(response);
    }

    private async Task<TicketSnapshot?> LoadSnapshotAsync(
        string ticketNo,
        string qrCode,
        CancellationToken cancellationToken) => await BuildSnapshotQuery(
            ticket => ticket.ETicketNo == ticketNo && ticket.QrCode == qrCode)
        .SingleOrDefaultAsync(cancellationToken);

    private async Task<TicketSnapshot?> LoadSnapshotByIdAsync(
        long ticketId,
        CancellationToken cancellationToken) => await BuildSnapshotQuery(
            ticket => ticket.ETicketId == ticketId)
        .SingleOrDefaultAsync(cancellationToken);

    private IQueryable<TicketSnapshot> BuildSnapshotQuery(
        Expression<Func<ETicket, bool>> ticketPredicate) =>
        from ticket in dbContext.Set<ETicket>().AsNoTracking().Where(ticketPredicate)
        join item in dbContext.Set<OrderItem>().AsNoTracking()
            on ticket.OrderItemId equals item.OrderItemId
        join order in dbContext.Set<Order>().AsNoTracking()
            on item.OrderId equals order.OrderId
        join session in dbContext.Set<ShowSessionEntity>().AsNoTracking()
            on order.SessionId equals session.SessionId into sessions
        from session in sessions.DefaultIfEmpty()
        select new TicketSnapshot(
            ticket.ETicketId,
            ticket.ETicketNo,
            ticket.QrCode,
            ticket.TicketStatus,
            ticket.CheckTime,
            ticket.CheckDevice,
            ticket.CheckBy,
            item.OrderItemId,
            item.ItemStatus,
            order.OrderId,
            order.OrderStatus,
            order.SessionId,
            session == null ? null : session.StartTime,
            session == null ? null : session.EndTime);

    private OrderTicketResult<TicketRedemptionResponse>? EvaluateEligibility(
        TicketSnapshot snapshot,
        DateTime operationTime)
    {
        if (snapshot.OrderStatus is not ("ISSUED" or "PART_REFUND"))
        {
            return Conflict(
                "TICKET_ORDER_NOT_ELIGIBLE",
                "The order state does not allow ticket redemption.");
        }

        if (snapshot.ItemStatus != "NORMAL")
        {
            return Conflict(
                "TICKET_ITEM_NOT_ELIGIBLE",
                "The order item state does not allow ticket redemption.");
        }

        var stateFailure = snapshot.TicketStatus switch
        {
            "UNUSED" => null,
            "USED" => Conflict(
                "TICKET_ALREADY_USED",
                "The ticket has already been redeemed."),
            "REFUNDING" => Conflict(
                "TICKET_REFUNDING",
                "The ticket is being refunded."),
            "REFUNDED" => Conflict(
                "TICKET_REFUNDED",
                "The ticket has been refunded."),
            "EXCHANGING" => Conflict(
                "TICKET_EXCHANGING",
                "The ticket is being exchanged."),
            "EXCHANGED" => Conflict(
                "TICKET_EXCHANGED",
                "The ticket has been exchanged."),
            _ => Conflict(
                "TICKET_REDEMPTION_CONFLICT",
                "The ticket state does not allow redemption."),
        };
        if (stateFailure is not null)
        {
            return stateFailure;
        }

        if (snapshot.CheckTime.HasValue ||
            snapshot.CheckDevice is not null ||
            snapshot.CheckBy is not null)
        {
            return Conflict(
                "TICKET_REDEMPTION_CONFLICT",
                "The ticket contains inconsistent redemption data.");
        }

        if (!snapshot.SessionStartTime.HasValue ||
            !snapshot.SessionEndTime.HasValue)
        {
            return NotFound(
                "TICKET_SESSION_NOT_FOUND",
                "The ticket session does not exist.");
        }

        var sessionStart = AsUtc(snapshot.SessionStartTime.Value);
        var sessionEnd = AsUtc(snapshot.SessionEndTime.Value);
        if (sessionStart > operationTime.AddMinutes(
                redemptionOptions.OpenBeforeMinutes) ||
            sessionEnd < operationTime.AddMinutes(
                -redemptionOptions.CloseAfterMinutes))
        {
            return Invalid(
                "TICKET_REDEMPTION_WINDOW_INVALID",
                "The ticket cannot be redeemed at the current time.");
        }

        return null;
    }

    private static TicketRedemptionResponse ToResponse(TicketSnapshot snapshot) => new(
        snapshot.ETicketId,
        snapshot.ETicketNo,
        snapshot.OrderId,
        snapshot.OrderItemId,
        snapshot.SessionId,
        ETicketStatus.USED,
        AsUtc(snapshot.CheckTime!.Value),
        snapshot.CheckDevice!,
        snapshot.CheckBy!);

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
                "Ticket redemption audit failed for ticket {ETicketNo}.",
                auditEvent.Metadata?["ETicketNo"]);
        }
    }

    private static DateTime TruncateToMicroseconds(DateTime value)
    {
        var utc = AsUtc(value);
        return new DateTime(
            utc.Ticks - utc.Ticks % 10,
            DateTimeKind.Utc);
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static OrderTicketResult<TicketRedemptionResponse> Invalid(
        string code,
        string message) => OrderTicketResult<TicketRedemptionResponse>.Fail(
        OrderTicketFailure.InvalidRequest,
        code,
        message);

    private static OrderTicketResult<TicketRedemptionResponse> NotFound(
        string code,
        string message) => OrderTicketResult<TicketRedemptionResponse>.Fail(
        OrderTicketFailure.NotFound,
        code,
        message);

    private static OrderTicketResult<TicketRedemptionResponse> Conflict(
        string code,
        string message) => OrderTicketResult<TicketRedemptionResponse>.Fail(
        OrderTicketFailure.Conflict,
        code,
        message);

    private sealed record TicketSnapshot(
        long ETicketId,
        string ETicketNo,
        string QrCode,
        string TicketStatus,
        DateTime? CheckTime,
        string? CheckDevice,
        string? CheckBy,
        long OrderItemId,
        string ItemStatus,
        long OrderId,
        string OrderStatus,
        long SessionId,
        DateTime? SessionStartTime,
        DateTime? SessionEndTime);
}
