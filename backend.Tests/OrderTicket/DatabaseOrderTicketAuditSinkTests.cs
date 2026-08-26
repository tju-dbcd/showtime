using ShowtimeBackend.Services.OrderTicket;
using ShowtimeBackend.Services.UserPermission;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class DatabaseOrderTicketAuditSinkTests
{
    [Fact]
    public async Task WriteAsync_MapsTicketEventToSafeOperationLogRequest()
    {
        var writer = new RecordingWriter();
        var sink = new DatabaseOrderTicketAuditSink(writer);
        var occurredAt = new DateTime(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);

        await sink.WriteAsync(
            new OrderTicketAuditEvent(
                "PAYMENT_TICKET_ISSUED",
                100,
                "alice",
                2,
                occurredAt),
            CancellationToken.None);

        Assert.NotNull(writer.Request);
        Assert.Equal("TICKET", writer.Request.Module);
        Assert.Equal("PAYMENT_TICKET_ISSUED", writer.Request.OperationType);
        Assert.True(writer.Request.Succeeded);
        Assert.Equal("alice", writer.Request.UserName);
        Assert.Equal(occurredAt, writer.Request.OccurredAt);
    }

    private sealed class RecordingWriter : IOperationLogWriter
    {
        public OperationLogWriteRequest? Request { get; private set; }

        public ValueTask WriteAsync(
            OperationLogWriteRequest request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return ValueTask.CompletedTask;
        }
    }
}
