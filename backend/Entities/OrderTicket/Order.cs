using ShowtimeBackend.Entities.Base;
using ShowtimeBackend.Entities.UserPermission;

namespace ShowtimeBackend.Entities.OrderTicket;

public class Order : AuditableEntity
{
    public long OrderId { get; set; }

    public string OrderNo { get; set; } = null!;

    public long UserId { get; set; }

    public long SessionId { get; set; }

    public string OrderType { get; set; } = "NORMAL";

    public long? ParentOrderId { get; set; }

    public decimal TotalAmount { get; set; }

    public decimal DiscountAmount { get; set; }

    public int TicketCount { get; set; } = 1;

    public string OrderStatus { get; set; } = "PENDING_PAY";

    public DateTime ExpireTime { get; set; }

    public DateTime? PayTime { get; set; }

    public DateTime? IssueTime { get; set; }

    public DateTime? CancelTime { get; set; }

    public string Source { get; set; } = "WEB";

    public string? IpAddress { get; set; }

    public string? Remark { get; set; }

    public SysUser? User { get; set; }

    public Order? ParentOrder { get; set; }

    public ICollection<Order> ChildOrders { get; set; } = [];

    public ICollection<OrderItem> Items { get; set; } = [];

    public ICollection<Payment> Payments { get; set; } = [];

    public ICollection<RefundRequest> RefundRequests { get; set; } = [];

    public ICollection<ExchangeRequest> ExchangeRequests { get; set; } = [];
}
