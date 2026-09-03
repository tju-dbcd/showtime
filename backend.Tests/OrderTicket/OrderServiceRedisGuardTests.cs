using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.SeatZone;
using ShowtimeBackend.Entities.ShowSession;
using ShowtimeBackend.Services.OrderTicket;
using ShowtimeBackend.Services.SeatZone;

namespace ShowtimeBackend.Tests.OrderTicket;

/// <summary>
/// OrderService 下单转换成功后释放 Redis 座位锁的行为单测（fake guard，不依赖真实 Redis）。
/// </summary>
public sealed class OrderServiceRedisGuardTests
{
    [Fact]
    public async Task CreateAsync_ReleasesGuardKeysForConvertedLocks()
    {
        await using var db = CreateDbContext();
        db.AddRange(
            new ShowSession { SessionId = 10, ShowId = 20, SeatMapId = 30 },
            new SeatSection { SeatSectionId = 40, SeatMapId = 30, SectionCode = "A", SectionName = "A区" },
            new Seat { SeatId = 50, SeatSectionId = 40, RowCode = "1", SeatNo = "1", IsSellable = true, SeatStatus = "ENABLED" },
            new Seat { SeatId = 51, SeatSectionId = 40, RowCode = "1", SeatNo = "2", IsSellable = true, SeatStatus = "ENABLED" },
            new PriceStrategy { PriceStrategyId = 60, SessionId = 10, SeatSectionId = 40, Price = 188m, Status = "ENABLED" });
        await db.SaveChangesAsync();
        var now = new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
        await AddActiveLockAsync(db, 50, "lock-50", now.UtcDateTime);
        await AddActiveLockAsync(db, 51, "lock-51", now.UtcDateTime);
        var guard = new FakeSeatLockGuard();
        var service = new OrderService(db, new FixedTimeProvider(now), guard);
        var request = new CreateOrderRequest(
            10,
            [
                new CreateOrderItemRequest(50, 60, null, "lock-50"),
                new CreateOrderItemRequest(51, 60, null, "lock-51")
            ],
            "靠近过道");

        var result = await service.CreateAsync(7, "alice", request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        // 下单成功后按 座位×场次×LockToken 释放 Redis 锁。
        Assert.Equal(
            [(10L, 50L, "lock-50"), (10L, 51L, "lock-51")],
            guard.ReleaseCalls);
        Assert.All(await db.SeatLocks.ToListAsync(), item =>
            Assert.Equal("CONVERTED", item.LockStatus));
    }

    [Fact]
    public async Task CreateAsync_DoesNotReleaseGuardKeysWhenLockInvalid()
    {
        await using var db = CreateDbContext();
        db.AddRange(
            new ShowSession { SessionId = 10, ShowId = 20, SeatMapId = 30 },
            new SeatSection { SeatSectionId = 40, SeatMapId = 30, SectionCode = "A", SectionName = "A区" },
            new Seat { SeatId = 50, SeatSectionId = 40, RowCode = "1", SeatNo = "1", IsSellable = true, SeatStatus = "ENABLED" },
            new PriceStrategy { PriceStrategyId = 60, SessionId = 10, SeatSectionId = 40, Price = 188m, Status = "ENABLED" });
        await db.SaveChangesAsync();
        var now = new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
        var guard = new FakeSeatLockGuard();
        var service = new OrderService(db, new FixedTimeProvider(now), guard);

        var result = await service.CreateAsync(
            7,
            "alice",
            new CreateOrderRequest(
                10,
                [new CreateOrderItemRequest(50, 60, null, "missing-lock")],
                null),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "ORDER_SEAT_LOCK_INVALID",
            result.ErrorCode);
        Assert.Empty(guard.ReleaseCalls);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static async Task AddActiveLockAsync(
        AppDbContext db,
        long seatId,
        string token,
        DateTime now)
    {
        db.Add(new SeatLock
        {
            SessionId = 10,
            SeatId = seatId,
            UserId = 7,
            LockToken = token,
            LockStatus = "ACTIVE",
            LockTime = now.AddMinutes(-1),
            ExpireTime = now.AddMinutes(9)
        });
        await db.SaveChangesAsync();
    }

    private sealed class FakeSeatLockGuard : ISeatLockGuard
    {
        public List<(long SessionId, long SeatId, string Token)> ReleaseCalls { get; } = [];
        public int AcquireCalls { get; private set; }

        public Task<SeatLockGuardAcquireResult> TryAcquireAsync(
            long sessionId,
            IReadOnlyCollection<SeatLock> locks,
            TimeSpan ttl,
            CancellationToken cancellationToken)
        {
            AcquireCalls++;
            return Task.FromResult(SeatLockGuardAcquireResult.Acquired);
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
