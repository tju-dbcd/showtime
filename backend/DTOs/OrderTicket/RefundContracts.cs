using ShowtimeBackend.Common;

namespace ShowtimeBackend.DTOs.OrderTicket;

public sealed record RefundQuoteRequest(IReadOnlyList<long> OrderItemIds);

public sealed record CreateRefundRequest(IReadOnlyList<long> OrderItemIds, string Reason);

public sealed record RefundQuoteItemResponse(
    long OrderItemId,
    decimal RefundBaseAmount);

public sealed record RefundQuoteResponse(
    DateTime QuotedAt,
    long OrderId,
    RefundType RefundType,
    long AppliedPolicyId,
    string PolicyName,
    decimal RefundAmount,
    decimal FeeRate,
    decimal AppliedServiceFee,
    decimal ActualRefund,
    IReadOnlyList<RefundQuoteItemResponse> Items);
