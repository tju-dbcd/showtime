using ShowtimeBackend.Entities.SeatZone;

namespace ShowtimeBackend.Services.SeatZone;

/// <summary>Redis 前置守卫批量获取座位锁的结果。</summary>
public enum SeatLockGuardAcquireResult
{
    /// <summary>全部座位均在 Redis 中获取成功，可继续走 Oracle 写事务。</summary>
    Acquired,

    /// <summary>至少一个座位已被其他请求持有（Redis 冲突），整批失败并返回 409。</summary>
    Conflict,

    /// <summary>Redis 不可用（连接失败或命令异常），守卫已跳过，调用方应降级为纯 Oracle 流程。</summary>
    Unavailable
}

/// <summary>
/// 选座锁的快速互斥层：在 Oracle 唯一索引仲裁之前先用 Redis 判定。
/// Redis 只是加速层，SEAT_LOCK 表仍是锁的真相源与审计记录；Redis 不可用时必须自动降级，购票不被阻断。
/// </summary>
public interface ISeatLockGuard
{
    /// <summary>
    /// 批量获取同一场次多个座位的 Redis 锁（key = showtime:seatlock:{sessionId}:{seatId}，
    /// value 复用 SEAT_LOCK.LockToken，TTL 与 DB 锁期一致由调用方传入）。
    /// 全部成功返回 <see cref="SeatLockGuardAcquireResult.Acquired"/>；
    /// 任一座位已被他人持有则回滚已获取部分并返回 <see cref="SeatLockGuardAcquireResult.Conflict"/>；
    /// Redis 异常时返回 <see cref="SeatLockGuardAcquireResult.Unavailable"/>（调用方跳过守卫继续纯 Oracle 流程）。
    /// </summary>
    Task<SeatLockGuardAcquireResult> TryAcquireAsync(
        long sessionId,
        IReadOnlyCollection<SeatLock> locks,
        TimeSpan ttl,
        CancellationToken cancellationToken);

    /// <summary>按锁 token 释放一把座位锁（内部比对 token，防止误删他人新获取的锁）。</summary>
    Task ReleaseAsync(long sessionId, long seatId, string token);
}