using ShowtimeBackend.Entities.Base;
using ShowtimeBackend.Entities.UserPermission;

namespace ShowtimeBackend.Entities.OrderTicket;

public class ExchangeRequest : AuditableEntity
{
    public long ExchangeId { get; set; }

    public string ExchangeNo { get; set; } = null!;

    public long OrderId { get; set; }

    public long UserId { get; set; }

    public long OrigSessionId { get; set; }

    public long TargetSessionId { get; set; }

    public string? ExchangeReason { get; set; }

    public decimal ExchangeFee { get; set; }

    public decimal PriceDiff { get; set; }

    public long? AppliedPolicyId { get; set; }

    public string ApproveStatus { get; set; } = "PENDING";

    public string? ReviewBy { get; set; }

    public DateTime? ReviewTime { get; set; }

    public string? ReviewRemark { get; set; }

    public string ExchangeStatus { get; set; } = "PENDING";

    public DateTime? CompleteTime { get; set; }

    public Order? Order { get; set; }

    public ExchangePolicy? AppliedPolicy { get; set; }

    public SysUser? User { get; set; }

    public ICollection<ExchangeItem> Items { get; set; } = [];
}
