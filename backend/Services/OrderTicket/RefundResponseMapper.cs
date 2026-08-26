using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.OrderTicket;

namespace ShowtimeBackend.Services.OrderTicket;

public static class RefundResponseMapper
{
    public static RefundResponse ToResponse(
        RefundRequest refundRequest,
        string? policyName) => new(
        refundRequest.RefundId,
        refundRequest.RefundNo,
        refundRequest.OrderId,
        refundRequest.UserId,
        refundRequest.RefundType.ToEnum<RefundType>(),
        refundRequest.RefundReason,
        refundRequest.AppliedPolicyId,
        policyName,
        refundRequest.RefundAmount,
        refundRequest.FeeRate,
        refundRequest.AppliedServiceFee,
        refundRequest.ActualRefund,
        refundRequest.ApproveStatus.ToEnum<RefundApproveStatus>(),
        refundRequest.RefundStatus.ToEnum<RefundStatus>(),
        refundRequest.ReviewBy,
        refundRequest.ReviewTime,
        refundRequest.ReviewRemark,
        refundRequest.CompleteTime,
        refundRequest.CreateTime,
        refundRequest.Items
            .OrderBy(item => item.OrderItemId)
            .Select(item => new RefundItemResponse(
                item.RefundItemId,
                item.OrderItemId,
                item.RefundBaseAmount,
                item.OrderItem!.ItemStatus.ToEnum<OrderItemStatus>(),
                item.OrderItem.ETicket!.TicketStatus.ToEnum<ETicketStatus>()))
            .ToList());
}
