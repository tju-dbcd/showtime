using System.Text.Json;
using System.Text.Json.Serialization;
using ShowtimeBackend.Entities.OrderTicket;

namespace ShowtimeBackend.Services.OrderTicket.Messaging;

public sealed record OrderCreatedEvent(
    string EventId,
    string EventType,
    DateTime OccurredAt,
    long OrderId,
    string OrderNo,
    long UserId,
    long SessionId,
    decimal TotalAmount,
    int TicketCount,
    string OrderStatus)
{
    public const string TypeName = "OrderCreated.v1";
    public const string RoutingKeyName = "order.created.v1";

    internal static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions);

    public static OrderCreatedEvent Create(Order order, DateTime occurredAt)
    {
        var eventId = Guid.NewGuid().ToString("D");
        return new OrderCreatedEvent(
            eventId,
            TypeName,
            DateTime.SpecifyKind(occurredAt, DateTimeKind.Utc),
            order.OrderId,
            order.OrderNo,
            order.UserId,
            order.SessionId,
            order.TotalAmount,
            order.TicketCount,
            order.OrderStatus);
    }
}
