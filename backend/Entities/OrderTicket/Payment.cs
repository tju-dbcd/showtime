using ShowtimeBackend.Entities.Base;
using ShowtimeBackend.Entities.UserPermission;

namespace ShowtimeBackend.Entities.OrderTicket;

public class Payment : AuditableEntity
{
    public long PaymentId { get; set; }

    public string PaymentNo { get; set; } = null!;

    public long OrderId { get; set; }

    public long UserId { get; set; }

    public decimal PayAmount { get; set; }

    public string PayChannel { get; set; } = null!;

    public string PayStatus { get; set; } = "PENDING";

    public string? TradeNo { get; set; }

    public string? CallbackData { get; set; }

    public DateTime? CallbackTime { get; set; }

    public DateTime? PayTime { get; set; }

    public decimal RefundAmount { get; set; }

    public Order? Order { get; set; }

    public SysUser? User { get; set; }
}
