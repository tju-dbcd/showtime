using ShowtimeBackend.Common;

namespace ShowtimeBackend.DTOs.OrderTicket;

public sealed record ExchangeTargetItemRequest(
    long OriginalOrderItemId,
    long SeatId,
    long PriceStrategyId,
    string LockToken);

public sealed record ExchangeQuoteRequest(
    long TargetSessionId,
    IReadOnlyList<ExchangeTargetItemRequest> TargetItems);

public sealed record CreateExchangeRequest(
    long TargetSessionId,
    IReadOnlyList<ExchangeTargetItemRequest> TargetItems,
    string? Reason);

public sealed record ExchangeQuoteItemResponse(
    long OriginalOrderItemId,
    long TargetSeatId,
    long TargetPriceStrategyId,
    long? RealNameId,
    decimal OriginalUnitPrice,
    decimal NewUnitPrice);

public sealed record ExchangeQuoteResponse(
    DateTime QuotedAt,
    long OrderId,
    long OrigSessionId,
    long TargetSessionId,
    decimal OrigDeduction,
    decimal TargetAmount,
    decimal PriceDiff,
    decimal ExchangeFee,
    decimal AmountDue,
    long AppliedPolicyId,
    string PolicyName,
    IReadOnlyList<ExchangeQuoteItemResponse> Items);

public sealed record ExchangeItemResponse(
    long ExchangeItemId,
    long OriginalOrderItemId,
    long NewOrderItemId,
    long TargetSeatId,
    long TargetPriceStrategyId,
    long? RealNameId,
    decimal OriginalUnitPrice,
    decimal NewUnitPrice,
    OrderItemStatus OriginalItemStatus,
    ETicketStatus OriginalTicketStatus,
    OrderItemStatus NewItemStatus,
    ETicketStatus? NewTicketStatus);

public sealed record ExchangeResponse(
    long ExchangeId,
    string ExchangeNo,
    long OriginalOrderId,
    long ChildOrderId,
    long UserId,
    long OrigSessionId,
    long TargetSessionId,
    string? Reason,
    decimal OrigDeduction,
    decimal TargetAmount,
    decimal PriceDiff,
    decimal ExchangeFee,
    decimal AmountDue,
    long? AppliedPolicyId,
    string? PolicyName,
    ExchangeApproveStatus ApproveStatus,
    ExchangeStatus ExchangeStatus,
    string? ReviewBy,
    DateTime? ReviewTime,
    string? ReviewRemark,
    DateTime? CompleteTime,
    DateTime ExpireTime,
    DateTime CreateTime,
    IReadOnlyList<ExchangeItemResponse> Items);

public sealed record ExchangeListQuery(
    ExchangeApproveStatus? ApproveStatus,
    ExchangeStatus? ExchangeStatus,
    int Page = 1,
    int PageSize = 20);

public sealed record AdminExchangeListQuery(
    ExchangeApproveStatus? ApproveStatus,
    ExchangeStatus? ExchangeStatus,
    long? OriginalOrderId,
    long? UserId,
    string? ExchangeNo,
    int Page = 1,
    int PageSize = 20);

public sealed record ExchangeSummaryResponse(
    long ExchangeId,
    string ExchangeNo,
    long OriginalOrderId,
    long ChildOrderId,
    decimal AmountDue,
    ExchangeApproveStatus ApproveStatus,
    ExchangeStatus ExchangeStatus,
    DateTime ExpireTime,
    DateTime CreateTime,
    DateTime? CompleteTime);

public sealed record PagedExchangeResponse(
    IReadOnlyList<ExchangeSummaryResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record ApproveExchangeRequest(string? Remark);

public sealed record RejectExchangeRequest(string Remark);

public sealed record ExchangePaymentRequest(
    PaymentChannel PayChannel,
    PaymentResult Result);

public sealed record ExchangePaymentResponse(
    PaymentResponse Payment,
    ExchangeResponse Exchange);
