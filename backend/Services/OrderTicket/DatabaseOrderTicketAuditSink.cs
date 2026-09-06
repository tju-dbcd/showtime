using ShowtimeBackend.Services.UserPermission;

namespace ShowtimeBackend.Services.OrderTicket;

public sealed class DatabaseOrderTicketAuditSink(
    IOperationLogWriter operationLogWriter) : IOrderTicketAuditSink
{
    public ValueTask WriteAsync(
        OrderTicketAuditEvent auditEvent,
        CancellationToken cancellationToken) => operationLogWriter.WriteAsync(
        new OperationLogWriteRequest(
            Module: "TICKET",
            OperationType: auditEvent.Operation,
            Succeeded: true,
            UserName: auditEvent.Actor,
            RequestSummary: new
            {
                auditEvent.OrderId,
                auditEvent.TicketCount,
                auditEvent.OccurredAt,
            },
            OccurredAt: auditEvent.OccurredAt),
        cancellationToken);
}
