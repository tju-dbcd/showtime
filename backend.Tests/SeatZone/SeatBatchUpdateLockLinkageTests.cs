using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.SeatZone;
using ShowtimeBackend.Entities.SeatZone;
using ShowtimeBackend.Entities.ShowSession;
using ShowtimeBackend.Services.SeatZone;

namespace ShowtimeBackend.Tests.SeatZone;

/// <summary>
/// 座位批量编辑与锁座/占座的联动回归测试：
/// 批量置不可售/禁用后，用户锁座必须被拒绝；批量更新不得破坏已有活动锁。
/// </summary>
public sealed class SeatBatchUpdateLockLinkageTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task BatchDisableSeat_BlocksSubsequentLock()
    {
        await using var db = CreateDbContext();
        await SeedSellableSessionAsync(db);
        var batchService = new SeatAdminService(db);
        var lockService = new SeatLockService(db, new FixedTimeProvider(Now));

        var batch = await batchService.UpdateSeatsAsync(
            40,
            new SeatBatchUpdateRequest([50, 51], null, "DISABLED", null, null),
            CancellationToken.None);
        Assert.True(batch.IsSuccess);
        Assert.Equal(2, batch.Data!.UpdatedCount);

        var lockResult = await lockService.LockAsync(
            7,
            "alice",
            10,
            new SeatLockBatchRequest([50]),
            CancellationToken.None);
        Assert.False(lockResult.IsSuccess);
        Assert.Equal("SEAT_LOCK_SEAT_UNAVAILABLE", lockResult.ErrorCode);
        Assert.Empty(await db.SeatLocks.ToListAsync());
    }

    [Fact]
    public async Task BatchMarkUnsellable_BlocksSubsequentLock()
    {
        await using var db = CreateDbContext();
        await SeedSellableSessionAsync(db);
        var batchService = new SeatAdminService(db);
        var lockService = new SeatLockService(db, new FixedTimeProvider(Now));

        var batch = await batchService.UpdateSeatsAsync(
            40,
            new SeatBatchUpdateRequest([50], null, null, null, false),
            CancellationToken.None);
        Assert.True(batch.IsSuccess);
        Assert.Equal(1, batch.Data!.UpdatedCount);

        var lockResult = await lockService.LockAsync(
            7,
            "alice",
            10,
            new SeatLockBatchRequest([50]),
            CancellationToken.None);
        Assert.False(lockResult.IsSuccess);
        Assert.Equal("SEAT_LOCK_SEAT_UNAVAILABLE", lockResult.ErrorCode);
    }

    [Fact]
    public async Task BatchUpdate_KeepsExistingActiveLocks()
    {
        await using var db = CreateDbContext();
        await SeedSellableSessionAsync(db);
        db.SeatLocks.Add(new SeatLock
        {
            SeatLockId = 1,
            SessionId = 10,
            SeatId = 50,
            UserId = 7,
            LockToken = "token-50",
            LockStatus = "ACTIVE",
            LockTime = Now.UtcDateTime.AddMinutes(-1),
            ExpireTime = Now.UtcDateTime.AddMinutes(9)
        });
        await db.SaveChangesAsync();
        var batchService = new SeatAdminService(db);

        var batch = await batchService.UpdateSeatsAsync(
            40,
            new SeatBatchUpdateRequest([50, 51], null, "MAINTENANCE", true, false),
            CancellationToken.None);

        Assert.True(batch.IsSuccess);
        var seatLock = await db.SeatLocks.SingleAsync();
        Assert.Equal("ACTIVE", seatLock.LockStatus);
        Assert.Equal("token-50", seatLock.LockToken);
    }

    private static AppDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

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

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
