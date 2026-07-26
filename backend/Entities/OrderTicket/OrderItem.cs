using ShowtimeBackend.Entities.Base;
using ShowtimeBackend.Entities.UserPermission;

namespace ShowtimeBackend.Entities.OrderTicket;

public class OrderItem : AuditableEntity
{
    public long OrderItemId { get; set; }

    public long OrderId { get; set; }

    public long SeatId { get; set; }

    public long PriceStrategyId { get; set; }

    public long? RealNameId { get; set; }

    public decimal UnitPrice { get; set; }

    public string ItemStatus { get; set; } = "NORMAL";

    public Order? Order { get; set; }

    public UserRealName? RealName { get; set; }

    public ETicket? ETicket { get; set; }

    public RefundItem? RefundItem { get; set; }

    public ExchangeItem? OriginalExchangeItem { get; set; }

    public ICollection<ExchangeItem> NewExchangeItems { get; set; } = [];
}
