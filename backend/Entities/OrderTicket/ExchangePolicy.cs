using ShowtimeBackend.Entities.Base;

namespace ShowtimeBackend.Entities.OrderTicket;

public class ExchangePolicy : AuditableEntity
{
    public long PolicyId { get; set; }

    public long? ShowId { get; set; }

    public string PolicyName { get; set; } = null!;

    public int ExchangeDeadlineHour { get; set; }

    public decimal ExchangeFee { get; set; }

    public byte AllowCrossSession { get; set; } = 1;

    public int Priority { get; set; } = 1;

    public byte Status { get; set; } = 1;

    public string? Remark { get; set; }
}
