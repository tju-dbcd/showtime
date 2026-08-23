namespace ShowtimeBackend.Services.OrderTicket;

public sealed record OrderTicketAuditEvent(
    string Operation,
    long OrderId,
    string Actor,
    int TicketCount,
    DateTime OccurredAt);

public interface IOrderTicketAuditSink
{
    ValueTask WriteAsync(
        OrderTicketAuditEvent auditEvent,
        CancellationToken cancellationToken);
}
