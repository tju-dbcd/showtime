using ShowtimeBackend.Entities.Base;

namespace ShowtimeBackend.Entities.SeatZone;

public class SeatLock : AuditableEntity
{
    public long SeatLockId { get; set; }
    public long SessionId { get; set; }
    public long SeatId { get; set; }
    public long UserId { get; set; }
    public string LockToken { get; set; } = null!;
    public string LockStatus { get; set; } = "ACTIVE";
    public DateTime LockTime { get; set; }
    public DateTime ExpireTime { get; set; }
    public DateTime? ReleaseTime { get; set; }
    public string? Remark { get; set; }
}
