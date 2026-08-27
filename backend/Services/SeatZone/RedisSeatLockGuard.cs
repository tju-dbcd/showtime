using StackExchange.Redis;
using ShowtimeBackend.Entities.SeatZone;

namespace ShowtimeBackend.Services.SeatZone;

/// <summary>
/// Redis 座位锁原语的薄封装，只暴露锁座需要的两个操作，便于单测注入 fake。
/// </summary>
internal interface ISeatLockGuardCommands
{
    Task<bool> LockTakeAsync(RedisKey key, RedisValue value, TimeSpan expiry);

    Task<bool> LockReleaseAsync(RedisKey key, RedisValue value);
}

internal sealed class RedisDatabaseCommands(IDatabase database) : ISeatLockGuardCommands
{
    public Task<bool> LockTakeAsync(RedisKey key, RedisValue value, TimeSpan expiry)
        => database.LockTakeAsync(key, value, expiry);

    public Task<bool> LockReleaseAsync(RedisKey key, RedisValue value)
        => database.LockReleaseAsync(key, value);
}

/// <summary>
/// 基于 StackExchange.Redis 原生 LockTake/LockRelease（SET NX PX + Lua 比对删除）实现的
/// 选座分布式锁守卫。锁粒度 = 座位×场次，key 的 value 复用 SEAT_LOCK.LockToken，
/// 保证释放时只有锁的持有者能删除（token 不匹配不误删）。
/// Redis 只是快速互斥层：获取失败/异常一律不写失败，由调用方决定 409 或降级；
/// TTL 为兜底，即使 key 因异常未删除也会自然过期，与 DB 侧 EXPIRE_TIME 过期迁移双通道自愈。
/// </summary>
public sealed class RedisSeatLockGuard : ISeatLockGuard
{
    private const string KeyPrefix = "showtime:seatlock:";

    private readonly ISeatLockGuardCommands _commands;
    private readonly ILogger _logger;

    public RedisSeatLockGuard(
        IConnectionMultiplexer multiplexer,
        ILogger<RedisSeatLockGuard> logger)
        : this(new RedisDatabaseCommands(multiplexer.GetDatabase()), logger)
    {
    }

    internal RedisSeatLockGuard(ISeatLockGuardCommands commands, ILogger logger)
    {
        _commands = commands;
        _logger = logger;
    }

    public async Task<SeatLockGuardAcquireResult> TryAcquireAsync(
        long sessionId,
        IReadOnlyCollection<SeatLock> locks,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var acquired = new List<SeatLock>(locks.Count);
        try
        {
            foreach (var seatLock in locks)
            {
                var taken = await _commands.LockTakeAsync(
                    Key(sessionId, seatLock.SeatId),
                    seatLock.LockToken,
                    ttl);
                if (!taken)
                {
                    // 任一座位已被他人持有：回滚已获取的部分，整批失败，语义与现有"任一座位冲突整批失败"一致。
                    await RollbackAsync(sessionId, acquired);
                    return SeatLockGuardAcquireResult.Conflict;
                }

                acquired.Add(seatLock);
                cancellationToken.ThrowIfCancellationRequested();
            }

            return SeatLockGuardAcquireResult.Acquired;
        }
        catch (OperationCanceledException)
        {
            // 请求被取消：已获取的 key 靠 TTL 自然过期兜底，不额外做 I/O。
            throw;
        }
        catch (RedisException exception)
        {
            // Redis 挂了/命令超时：尽力回滚已获取的 key，通知调用方降级走纯 Oracle 流程。
            _logger.LogWarning(exception, "Redis seat lock guard unavailable; degrade to Oracle-only flow.");
            await RollbackAsync(sessionId, acquired);
            return SeatLockGuardAcquireResult.Unavailable;
        }
    }

    public async Task ReleaseAsync(long sessionId, long seatId, string token)
    {
        try
        {
            await _commands.LockReleaseAsync(Key(sessionId, seatId), token);
        }
        catch (RedisException exception)
        {
            // 释放失败不阻断业务，key 由 TTL 自然过期。
            _logger.LogWarning(
                exception,
                "Redis seat lock release failed for session {SessionId} seat {SeatId}; the key will expire by TTL.",
                sessionId,
                seatId);
        }
    }

    private async Task RollbackAsync(long sessionId, IReadOnlyCollection<SeatLock> acquired)
    {
        foreach (var seatLock in acquired)
        {
            try
            {
                await _commands.LockReleaseAsync(
                    Key(sessionId, seatLock.SeatId),
                    seatLock.LockToken);
            }
            catch (RedisException exception)
            {
                // 回滚失败时 key 由 TTL 自然过期，不影响正确性。
                _logger.LogWarning(
                    exception,
                    "Redis seat lock rollback failed for session {SessionId} seat {SeatId}; the key will expire by TTL.",
                    sessionId,
                    seatLock.SeatId);
            }
        }
    }

    private static RedisKey Key(long sessionId, long seatId)
        => $"{KeyPrefix}{sessionId}:{seatId}";
}