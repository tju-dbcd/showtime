using ShowtimeBackend.Entities.Base;
using ShowtimeBackend.Entities.UserPermission;

namespace ShowtimeBackend.Entities.OrderTicket;

public class RefundRequest : AuditableEntity
{
    public long RefundId { get; set; }

    public string RefundNo { get; set; } = null!;

    public long OrderId { get; set; }

    public long UserId { get; set; }

    public string RefundType { get; set; } = "FULL";

    public string? RefundReason { get; set; }

    public decimal RefundAmount { get; set; }

    public decimal? ActualRefund { get; set; }

    public decimal FeeRate { get; set; }

    public long? AppliedPolicyId { get; set; }

    public decimal AppliedServiceFee { get; set; }

    public string ApproveStatus { get; set; } = "PENDING";

    public string? ReviewBy { get; set; }

    public DateTime? ReviewTime { get; set; }

    public string? ReviewRemark { get; set; }

    public string RefundStatus { get; set; } = "PENDING";

    public DateTime? CompleteTime { get; set; }

    public Order? Order { get; set; }

    public RefundPolicy? AppliedPolicy { get; set; }

    public SysUser? User { get; set; }

    public ICollection<RefundItem> Items { get; set; } = [];
}
