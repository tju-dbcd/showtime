using ShowtimeBackend.Entities.Base;
using ShowtimeBackend.Entities.UserPermission;

namespace ShowtimeBackend.Entities.OrderTicket;

public class ETicket : AuditableEntity
{
    public long ETicketId { get; set; }

    public string ETicketNo { get; set; } = null!;

    public long OrderItemId { get; set; }

    public long UserId { get; set; }

    public string QrCode { get; set; } = null!;

    public string AntiFakeCode { get; set; } = null!;

    public string TicketStatus { get; set; } = "UNUSED";

    public DateTime? CheckTime { get; set; }

    public string? CheckDevice { get; set; }

    public string? CheckBy { get; set; }

    public OrderItem? OrderItem { get; set; }

    public SysUser? User { get; set; }
}
