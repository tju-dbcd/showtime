using Microsoft.Extensions.Logging.Abstractions;
using ShowtimeBackend.Entities.SeatZone;
using ShowtimeBackend.Services.SeatZone;
using StackExchange.Redis;

namespace ShowtimeBackend.Tests.SeatZone;

/// <summary>
/// RedisSeatLockGuard 单测：通过 fake 命令层验证批量获取、部分回滚、异常降级与按 token 释放，
/// 不依赖真实 Redis 进程。
/// </summary>
public sealed class RedisSeatLockGuardTests
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    [Fact]
    public async Task TryAcquireAsync_AllKeysTaken_ReturnsAcquired()
    {
        var commands = new FakeCommands();
        var guard = new RedisSeatLockGuard(commands, NullLogger.Instance);

        var result = await guard.TryAcquireAsync(
            10, CreateLocks((50, "token-50"), (51, "token-51")), Ttl, CancellationToken.None);

        Assert.Equal(SeatLockGuardAcquireResult.Acquired, result);
        Assert.Equal(
            [
                ("showtime:seatlock:10:50", "token-50", Ttl),
                ("showtime:seatlock:10:51", "token-51", Ttl)
            ],
            commands.AcquireCalls);
        Assert.Empty(commands.ReleaseCalls);
    }

    [Fact]
    public async Task TryAcquireAsync_PartialConflict_RollsBackAcquiredKeys()
    {
        var commands = new FakeCommands { ConflictedKeys = { "showtime:seatlock:10:51" } };
        var guard = new RedisSeatLockGuard(commands, NullLogger.Instance);

        var result = await guard.TryAcquireAsync(
            10, CreateLocks((50, "token-50"), (51, "token-51")), Ttl, CancellationToken.None);

        Assert.Equal(SeatLockGuardAcquireResult.Conflict, result);
        // 只回滚已获取的第一把锁，且按各自的 token 释放。
        Assert.Equal(
            [("showtime:seatlock:10:50", "token-50")],
            commands.ReleaseCalls);
    }

    [Fact]
    public async Task TryAcquireAsync_RedisException_ReturnsUnavailableAndRollsBack()
    {
        var commands = new FakeCommands { ThrowingKeys = { "showtime:seatlock:10:51" } };
        var guard = new RedisSeatLockGuard(commands, NullLogger.Instance);

        var result = await guard.TryAcquireAsync(
            10, CreateLocks((50, "token-50"), (51, "token-51")), Ttl, CancellationToken.None);

        // Redis 异常不阻断购票：守卫报告不可用，由调用方降级走纯 Oracle 流程。
        Assert.Equal(SeatLockGuardAcquireResult.Unavailable, result);
        Assert.Equal(
            [("showtime:seatlock:10:50", "token-50")],
            commands.ReleaseCalls);
    }

    [Fact]
    public async Task ReleaseAsync_ReleasesKeyWithMatchingToken()
    {
        var commands = new FakeCommands();
        var guard = new RedisSeatLockGuard(commands, NullLogger.Instance);

        await guard.ReleaseAsync(10, 50, "token-50");

        Assert.Equal(
            [("showtime:seatlock:10:50", "token-50")],
            commands.ReleaseCalls);
    }

    [Fact]
    public async Task ReleaseAsync_RedisException_IsSwallowed()
    {
        var commands = new FakeCommands { ThrowOnRelease = true };
        var guard = new RedisSeatLockGuard(commands, NullLogger.Instance);

        // 释放失败不抛异常，key 交由 TTL 自然过期。
        await guard.ReleaseAsync(10, 50, "token-50");
    }

    private static SeatLock[] CreateLocks(params (long SeatId, string Token)[] items)
        => items.Select(item => new SeatLock
        {
            SessionId = 10,
            SeatId = item.SeatId,
            LockToken = item.Token
        }).ToArray();

    private sealed class FakeCommands : ISeatLockGuardCommands
    {
        public List<(string Key, string Value, TimeSpan Expiry)> AcquireCalls { get; } = [];
        public List<(string Key, string Value)> ReleaseCalls { get; } = [];
        public HashSet<string> ConflictedKeys { get; } = [];
        public HashSet<string> ThrowingKeys { get; } = [];
        public bool ThrowOnRelease { get; set; }

        public Task<bool> LockTakeAsync(RedisKey key, RedisValue value, TimeSpan expiry)
        {
            var keyText = key.ToString();
            if (ThrowingKeys.Contains(keyText))
            {
                throw new RedisException($"connection lost for {keyText}");
            }

            AcquireCalls.Add((keyText, value.ToString(), expiry));
            return Task.FromResult(!ConflictedKeys.Contains(keyText));
        }

        public Task<bool> LockReleaseAsync(RedisKey key, RedisValue value)
        {
            if (ThrowOnRelease)
            {
                throw new RedisException("connection lost");
            }

            ReleaseCalls.Add((key.ToString(), value.ToString()));
            return Task.FromResult(true);
        }
    }
}
