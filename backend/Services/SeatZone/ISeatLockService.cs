using ShowtimeBackend.DTOs.SeatZone;

namespace ShowtimeBackend.Services.SeatZone;

public interface ISeatLockService
{
    /// <summary>
    /// 为用户批量创建指定场次的临时座位锁。
    /// </summary>
    Task<SeatZoneResult<SeatLockBatchResponse>> LockAsync(
        long userId,
        string actor,
        long sessionId,
        SeatLockBatchRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// 根据锁令牌批量释放当前用户自己的有效锁。
    /// </summary>
    Task<SeatZoneResult<SeatLockReleaseResponse>> ReleaseAsync(
        long userId,
        string actor,
        long sessionId,
        SeatLockReleaseRequest request,
        CancellationToken cancellationToken);
}
