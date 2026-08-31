namespace ShowtimeBackend.DTOs.SeatZone;

/// <summary>
/// 批量锁定同一场次中的多个座位。
/// </summary>
public sealed record SeatLockBatchRequest(IReadOnlyList<long> SeatIds);

/// <summary>
/// 批量释放当前用户持有的座位锁。
/// </summary>
public sealed record SeatLockReleaseRequest(IReadOnlyList<string> LockTokens);

/// <summary>
/// 单个座位对应的锁令牌和过期时间。
/// </summary>
public sealed record SeatLockItemResponse(
    long SeatId,
    string LockToken,
    DateTime ExpireTime);

/// <summary>
/// 一次批量锁座的结果。
/// </summary>
public sealed record SeatLockBatchResponse(
    long SessionId,
    DateTime ExpireTime,
    IReadOnlyList<SeatLockItemResponse> Locks);

/// <summary>
/// 批量释放锁座的结果。
/// </summary>
public sealed record SeatLockReleaseResponse(
    long SessionId,
    int ReleasedCount);
