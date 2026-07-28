using ShowtimeBackend.Entities.Base;

namespace ShowtimeBackend.Entities.OrderTicket;

public class RefundPolicy : AuditableEntity
{
    public long PolicyId { get; set; }

    public long? ShowId { get; set; }

    public string PolicyName { get; set; } = null!;

    public int RefundDeadlineHour { get; set; }

    public decimal RefundRate { get; set; } = 1;

    public decimal ServiceFee { get; set; }

    public int Priority { get; set; } = 1;

    public byte Status { get; set; } = 1;

    public string? Remark { get; set; }
}
