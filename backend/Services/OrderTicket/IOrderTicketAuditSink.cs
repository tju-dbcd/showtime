namespace ShowtimeBackend.Services.OrderTicket;

public sealed record OrderTicketAuditEvent(
    string Operation,
    long OrderId,
    string Actor,
    int TicketCount,
    DateTime OccurredAt,
    long? RefundId = null,
    decimal? ActualRefund = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public interface IOrderTicketAuditSink
{
    ValueTask WriteAsync(
        OrderTicketAuditEvent auditEvent,
        CancellationToken cancellationToken);
}
