namespace ShowtimeBackend.Services.UserPermission;

public sealed record OperationLogWriteRequest(
    string Module,
    string OperationType,
    bool Succeeded,
    long? UserId = null,
    string? UserName = null,
    long? ShowId = null,
    long? CostTimeMilliseconds = null,
    object? RequestSummary = null,
    object? ResponseSummary = null,
    string? ErrorMessage = null,
    DateTime? OccurredAt = null);
