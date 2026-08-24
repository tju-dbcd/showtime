using ShowtimeBackend.Entities.Base;

namespace ShowtimeBackend.Entities.OrderTicket;

public class RefundItem : AuditableEntity
{
    public long RefundItemId { get; set; }

    public long RefundId { get; set; }

    public long OrderItemId { get; set; }

    public decimal RefundBaseAmount { get; set; }

    public RefundRequest? RefundRequest { get; set; }

    public OrderItem? OrderItem { get; set; }
}
