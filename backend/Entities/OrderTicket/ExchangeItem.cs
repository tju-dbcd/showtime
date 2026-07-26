using ShowtimeBackend.Entities.Base;

namespace ShowtimeBackend.Entities.OrderTicket;

public class ExchangeItem : AuditableEntity
{
    public long ExchangeItemId { get; set; }

    public long ExchangeId { get; set; }

    public long OrderItemId { get; set; }

    public long NewOrderItemId { get; set; }

    public ExchangeRequest? ExchangeRequest { get; set; }

    public OrderItem? OrderItem { get; set; }

    public OrderItem? NewOrderItem { get; set; }
}
