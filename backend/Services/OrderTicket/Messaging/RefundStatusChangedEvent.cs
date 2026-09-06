using System.Text.Json;

namespace ShowtimeBackend.Services.OrderTicket.Messaging;

// 用户侧退款状态实时通知：审核通过(PROCESSING)/完成(COMPLETED)/拒绝(FAILED) 均推送，
// 由前端 SignalR 消费后在订单详情/订单列表自动刷新状态。
public sealed record RefundStatusChangedEvent(
    string EventId,
    string EventType,
    DateTime OccurredAt,
    long RefundId,
    string RefundNo,
    long OrderId,
    long UserId,
    string ApproveStatus,
    string RefundStatus,
    decimal? ActualRefund)
{
    public const string TypeName = "RefundStatusChanged.v1";
    public const string RoutingKeyName = "refund.status-changed.v1";

    public string Serialize() => JsonSerializer.Serialize(
        this,
        OrderCreatedEvent.SerializerOptions);
}