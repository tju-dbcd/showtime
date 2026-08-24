namespace ShowtimeBackend.DTOs.OrderTicket;

public sealed record SaveRefundPolicyRequest(
    long? ShowId,
    string PolicyName,
    int RefundDeadlineHour,
    decimal RefundRate,
    decimal ServiceFee,
    int Priority,
    string? Remark);

public sealed record UpdateRefundPolicyStatusRequest(byte Status);

public sealed record RefundPolicyListQuery(
    long? ShowId,
    byte? Status,
    int Page = 1,
    int PageSize = 20);

public sealed record RefundPolicyResponse(
    long PolicyId,
    long? ShowId,
    string PolicyName,
    int RefundDeadlineHour,
    decimal RefundRate,
    decimal ServiceFee,
    int Priority,
    byte Status,
    string? Remark,
    DateTime CreateTime,
    DateTime UpdateTime);

public sealed record PagedRefundPolicyResponse(
    IReadOnlyList<RefundPolicyResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);
