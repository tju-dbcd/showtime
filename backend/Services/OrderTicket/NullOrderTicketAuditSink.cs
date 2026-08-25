namespace ShowtimeBackend.Services.OrderTicket;

public sealed class NullOrderTicketAuditSink : IOrderTicketAuditSink
{
    public ValueTask WriteAsync(
        OrderTicketAuditEvent auditEvent,
        CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
