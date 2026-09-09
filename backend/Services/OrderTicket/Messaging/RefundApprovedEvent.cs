using System.Text.Json;

namespace ShowtimeBackend.Services.OrderTicket.Messaging;

public sealed record RefundApprovedEvent(
    string EventId,
    string EventType,
    DateTime OccurredAt,
    long RefundId,
    string RefundNo,
    long OrderId,
    long UserId,
    decimal ActualRefund)
{
    public const string TypeName = "RefundApproved.v1";
    public const string RoutingKeyName = "refund.approved.v1";

    public string Serialize() => JsonSerializer.Serialize(
        this,
        OrderCreatedEvent.SerializerOptions);
}
