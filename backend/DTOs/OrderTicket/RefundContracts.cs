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

public sealed record RefundItemResponse(
    long RefundItemId,
    long OrderItemId,
    decimal RefundBaseAmount,
    OrderItemStatus ItemStatus,
    ETicketStatus TicketStatus);

public sealed record RefundResponse(
    long RefundId,
    string RefundNo,
    long OrderId,
    long UserId,
    RefundType RefundType,
    string? RefundReason,
    long? AppliedPolicyId,
    string? PolicyName,
    decimal RefundAmount,
    decimal FeeRate,
    decimal AppliedServiceFee,
    decimal? ActualRefund,
    RefundApproveStatus ApproveStatus,
    RefundStatus RefundStatus,
    string? ReviewBy,
    DateTime? ReviewTime,
    string? ReviewRemark,
    DateTime? CompleteTime,
    DateTime CreateTime,
    IReadOnlyList<RefundItemResponse> Items);
