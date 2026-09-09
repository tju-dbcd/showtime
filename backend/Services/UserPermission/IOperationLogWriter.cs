namespace ShowtimeBackend.Services.UserPermission;

public interface IOperationLogWriter
{
    ValueTask WriteAsync(
        OperationLogWriteRequest request,
        CancellationToken cancellationToken);
}

public static class OperationLogWriterExtensions
{
    public static async ValueTask WriteBestEffortAsync(
        this IOperationLogWriter writer,
        OperationLogWriteRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await writer.WriteAsync(request, cancellationToken);
        }
        catch
        {
            // The operation-log contract is deliberately best-effort. The concrete
            // database writer logs its own failures; this guard also protects callers
            // when a test double or a future implementation violates that contract.
        }
    }
}
