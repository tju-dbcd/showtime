using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.SeatZone;
using ShowtimeBackend.Entities.SeatZone;
using ShowtimeBackend.Entities.ShowSession;
using ShowtimeBackend.Services.SeatZone;

namespace ShowtimeBackend.Tests.SeatZone;

/// <summary>
/// SeatLockService 接入 Redis 守卫后的行为单测：守卫冲突 409 不写库、守卫异常降级仍成功、
/// 释放成功后按 token 释放 Redis 锁。通过 fake guard 注入，不依赖真实 Redis。
/// </summary>
public sealed class SeatLockServiceRedisGuardTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LockAsync_GuardConflict_ReturnsConflictWithoutWritingDatabase()
    {
        await using var db = CreateDbContext();
        await SeedSellableSessionAsync(db);
        var guard = FakeSeatLockGuard.WithResult(SeatLockGuardAcquireResult.Conflict);
        var service = new SeatLockService(db, new FixedTimeProvider(Now), guard);

        var result = await service.LockAsync(
            7,
            "alice",
            10,
            new SeatLockBatchRequest([50, 51]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SEAT_LOCK_CONFLICT", result.ErrorCode);
        Assert.Single(guard.AcquireCalls);
        Assert.Empty(guard.ReleaseCalls);
        Assert.Empty(await db.SeatLocks.ToListAsync());
    }

    [Fact]
    public async Task LockAsync_GuardUnavailable_DegradesToOracleAndStillSucceeds()
    {
        await using var db = CreateDbContext();
        await SeedSellableSessionAsync(db);
        var guard = FakeSeatLockGuard.WithResult(SeatLockGuardAcquireResult.Unavailable);
        var service = new SeatLockService(db, new FixedTimeProvider(Now), guard);

        var result = await service.LockAsync(
            7,
            "alice",
            10,
            new SeatLockBatchRequest([50, 51]),
            CancellationToken.None);

        // Redis 不可用时自动降级为纯 Oracle 流程，购票不被阻断。
        Assert.True(result.IsSuccess);
        Assert.Equal(2, (await db.SeatLocks.ToListAsync()).Count);
        Assert.Single(guard.AcquireCalls);
        Assert.Empty(guard.ReleaseCalls);
    }

    [Fact]
    public async Task LockAsync_GuardAcquired_WritesDatabaseAsUsual()
    {
        await using var db = CreateDbContext();
        await SeedSellableSessionAsync(db);
        var guard = FakeSeatLockGuard.WithResult(SeatLockGuardAcquireResult.Acquired);
        var service = new SeatLockService(db, new FixedTimeProvider(Now), guard);

        var result = await service.LockAsync(
            7,
            "alice",
            10,
            new SeatLockBatchRequest([50, 51]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, (await db.SeatLocks.ToListAsync()).Count);
        Assert.All(await db.SeatLocks.ToListAsync(), item => Assert.NotNull(item.LockToken));
        Assert.Single(guard.AcquireCalls);
        Assert.Empty(guard.ReleaseCalls);
    }

    [Fact]
    public async Task LockAsync_GuardDisabled_SkipsRedisEntirely()
    {
        await using var db = CreateDbContext();
        await SeedSellableSessionAsync(db);
        var guard = FakeSeatLockGuard.WithResult(SeatLockGuardAcquireResult.Conflict);
        var service = new SeatLockService(db, new FixedTimeProvider(Now), guard, guardEnabled: false);

        var result = await service.LockAsync(
            7,
            "alice",
            10,
            new SeatLockBatchRequest([50, 51]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(guard.AcquireCalls);
    }

    [Fact]
    public async Task ReleaseAsync_ReleasesGuardKeysAfterDatabaseCommit()
    {
        await using var db = CreateDbContext();
        db.AddRange(
            CreateActiveLock(70, 50, "token-50"),
            CreateActiveLock(71, 51, "token-51"));
        await db.SaveChangesAsync();
        var guard = FakeSeatLockGuard.WithResult(SeatLockGuardAcquireResult.Acquired);
        var service = new SeatLockService(db, new FixedTimeProvider(Now), guard);

        var result = await service.ReleaseAsync(
            7,
            "alice",
            10,
            new SeatLockReleaseRequest(["token-50", "token-51"]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [(10L, 50L, "token-50"), (10L, 51L, "token-51")],
            guard.ReleaseCalls);
    }

    [Fact]
    public async Task ReleaseAsync_DoesNotReleaseGuardKeysWhenDatabaseFails()
    {
        await using var db = CreateDbContext();
        db.Add(CreateActiveLock(70, 50, "token-50"));
        await db.SaveChangesAsync();
        var guard = FakeSeatLockGuard.WithResult(SeatLockGuardAcquireResult.Acquired);
        var service = new SeatLockService(db, new FixedTimeProvider(Now), guard);

        var result = await service.ReleaseAsync(
            7,
            "alice",
            10,
            new SeatLockReleaseRequest(["token-50", "missing-token"]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SEAT_LOCK_NOT_FOUND", result.ErrorCode);
        Assert.Empty(guard.ReleaseCalls);
        Assert.Equal("ACTIVE", (await db.SeatLocks.FindAsync(70L))!.LockStatus);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static async Task SeedSellableSessionAsync(AppDbContext db)
    {
        db.AddRange(
            new ShowSession
            {
                SessionId = 10,
                ShowId = 20,
                SeatMapId = 30,
                SaleStartTime = Now.UtcDateTime.AddHours(-1),
                SaleEndTime = Now.UtcDateTime.AddHours(1),
                SessionStatus = "ONSALE"
            },
            new SeatSection
            {
                SeatSectionId = 40,
                SeatMapId = 30,
                SectionCode = "A",
                SectionName = "A区",
                IsSellable = true
            },
            new Seat
            {
                SeatId = 50,
                SeatSectionId = 40,
                RowCode = "1",
                SeatNo = "1",
                IsSellable = true,
                SeatStatus = "ENABLED"
            },
            new Seat
            {
                SeatId = 51,
                SeatSectionId = 40,
                RowCode = "1",
                SeatNo = "2",
                IsSellable = true,
                SeatStatus = "ENABLED"
            });
        await db.SaveChangesAsync();
    }

    private static SeatLock CreateActiveLock(long id, long seatId, string token) => new()
    {
        SeatLockId = id,
        SessionId = 10,
        SeatId = seatId,
        UserId = 7,
        LockToken = token,
        LockStatus = "ACTIVE",
        LockTime = Now.UtcDateTime.AddMinutes(-1),
        ExpireTime = Now.UtcDateTime.AddMinutes(9)
    };

    private sealed class FakeSeatLockGuard(
        SeatLockGuardAcquireResult acquireResult) : ISeatLockGuard
    {
        public List<IReadOnlyCollection<SeatLock>> AcquireCalls { get; } = [];
        public List<(long SessionId, long SeatId, string Token)> ReleaseCalls { get; } = [];

        public static FakeSeatLockGuard WithResult(SeatLockGuardAcquireResult result)
            => new(result);

        public Task<SeatLockGuardAcquireResult> TryAcquireAsync(
            long sessionId,
            IReadOnlyCollection<SeatLock> locks,
            TimeSpan ttl,
            CancellationToken cancellationToken)
        {
            AcquireCalls.Add(locks);
            return Task.FromResult(acquireResult);
        }

        public Task ReleaseAsync(long sessionId, long seatId, string token)
        {
            ReleaseCalls.Add((sessionId, seatId, token));
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
