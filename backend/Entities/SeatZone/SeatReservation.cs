using ShowtimeBackend.Entities.Base;

namespace ShowtimeBackend.Entities.SeatZone;

/// <summary>
/// Stores a seat reservation after a lock is converted or a system reservation is created.
/// Order-item references remain scalar IDs until the order-ticket module is mapped.
/// </summary>
public class SeatReservation : AuditableEntity
{
    public long SeatReservationId { get; set; }

    /// <summary>
    /// 演出场次标识；由场次模块维护，当前仅保存关联值。
    /// </summary>
    public long SessionId { get; set; }
    public long SeatId { get; set; }

    /// <summary>
    /// 确认该保留后生成的订单明细标识；未下单时为空。
    /// </summary>
    public long? OrderItemId { get; set; }

    /// <summary>
    /// 该保留来源的占座记录；无占座直接保留时可为空。
    /// </summary>
    public long? SeatLockId { get; set; }

    /// <summary>
    /// 保留类型：ORDER-用户下单，SYSTEM-系统预留，VIP-VIP 预留。
    /// </summary>
    public string ReservationType { get; set; } = "ORDER";

    /// <summary>
    /// 保留状态：ACTIVE-保留中，CANCELLED-已取消，RELEASED-已释放。
    /// </summary>
    public string ReservationStatus { get; set; } = "ACTIVE";

    /// <summary>
    /// 创建座位保留的业务时间。
    /// </summary>
    public DateTime ReserveTime { get; set; }

    /// <summary>
    /// 取消保留的时间；保留未取消时为空。
    /// </summary>
    public DateTime? CancelTime { get; set; }

    /// <summary>
    /// 系统或人工保留座位时填写的原因。
    /// </summary>
    public string? HoldReason { get; set; }
    public string? Remark { get; set; }
}
