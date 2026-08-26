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

public sealed record RefundListQuery(
    RefundApproveStatus? ApproveStatus,
    RefundStatus? RefundStatus,
    int Page = 1,
    int PageSize = 20);

public sealed record RefundSummaryResponse(
    long RefundId,
    string RefundNo,
    long OrderId,
    RefundType RefundType,
    decimal? ActualRefund,
    RefundApproveStatus ApproveStatus,
    RefundStatus RefundStatus,
    DateTime CreateTime,
    DateTime? CompleteTime);

public sealed record PagedRefundResponse(
    IReadOnlyList<RefundSummaryResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);
