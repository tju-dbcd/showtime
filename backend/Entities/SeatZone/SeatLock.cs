using ShowtimeBackend.Entities.Base;

namespace ShowtimeBackend.Entities.SeatZone;

/// <summary>
/// Stores a temporary exclusive lock for one seat in one show session.
/// Session and user references remain scalar IDs until their owning modules are mapped.
/// </summary>
public class SeatLock : AuditableEntity
{
    public long SeatLockId { get; set; }

    /// <summary>
    /// 演出场次标识；由场次模块维护，当前仅保存关联值。
    /// </summary>
    public long SessionId { get; set; }
    public long SeatId { get; set; }

    /// <summary>
    /// 发起占座的用户标识；由用户模块维护，当前仅保存关联值。
    /// </summary>
    public long UserId { get; set; }

    /// <summary>
    /// 占座令牌，用于后续确认、释放或校验本次占座。
    /// </summary>
    public string LockToken { get; set; } = null!;

    /// <summary>
    /// 占座状态：ACTIVE-占用中，RELEASED-已释放，EXPIRED-已过期，CONVERTED-已转为保留。
    /// </summary>
    public string LockStatus { get; set; } = "ACTIVE";

    /// <summary>
    /// 写入占座记录的业务时间。
    /// </summary>
    public DateTime LockTime { get; set; }

    /// <summary>
    /// 占座有效期截止时间，到期后座位应可重新出售。
    /// </summary>
    public DateTime ExpireTime { get; set; }

    /// <summary>
    /// 实际释放占座的时间；未释放时为空。
    /// </summary>
    public DateTime? ReleaseTime { get; set; }
    public string? Remark { get; set; }
}
