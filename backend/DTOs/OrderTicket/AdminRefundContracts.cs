using ShowtimeBackend.Common;

namespace ShowtimeBackend.DTOs.OrderTicket;

public sealed record AdminRefundListQuery(
    RefundApproveStatus? ApproveStatus,
    RefundStatus? RefundStatus,
    long? OrderId,
    long? UserId,
    string? RefundNo,
    int Page = 1,
    int PageSize = 20);

public sealed record ApproveRefundRequest(string? Remark);

public sealed record RejectRefundRequest(string Remark);
