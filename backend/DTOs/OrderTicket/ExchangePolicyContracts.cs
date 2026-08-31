namespace ShowtimeBackend.DTOs.OrderTicket;

public sealed record SaveExchangePolicyRequest(
    long? ShowId,
    string PolicyName,
    int ExchangeDeadlineHour,
    decimal ExchangeFee,
    byte AllowCrossSession,
    int Priority,
    string? Remark);

public sealed record UpdateExchangePolicyStatusRequest(byte Status);

public sealed record ExchangePolicyListQuery(
    long? ShowId,
    byte? Status,
    int Page = 1,
    int PageSize = 20);

public sealed record ExchangePolicyResponse(
    long PolicyId,
    long? ShowId,
    string PolicyName,
    int ExchangeDeadlineHour,
    decimal ExchangeFee,
    byte AllowCrossSession,
    int Priority,
    byte Status,
    string? Remark,
    DateTime CreateTime,
    DateTime UpdateTime);

public sealed record PagedExchangePolicyResponse(
    IReadOnlyList<ExchangePolicyResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);
