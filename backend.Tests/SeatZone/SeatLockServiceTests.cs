using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.SeatZone;
using ShowtimeBackend.Entities.SeatZone;
using ShowtimeBackend.Entities.ShowSession;
using ShowtimeBackend.Services.SeatZone;

namespace ShowtimeBackend.Tests.SeatZone;

public sealed class SeatLockServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LockAsync_LocksAllSeatsForTenMinutes()
    {
        await using var db = CreateDbContext();
        await SeedSellableSessionAsync(db);
        var service = new SeatLockService(db, new FixedTimeProvider(Now));

        var result = await service.LockAsync(
            7,
            "alice",
            10,
            new SeatLockBatchRequest([50, 51]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now.UtcDateTime.AddMinutes(10), result.Value!.ExpireTime);
        Assert.Equal([50L, 51L], result.Value.Locks.Select(item => item.SeatId));
        Assert.Equal(2, result.Value.Locks.Select(item => item.LockToken).Distinct().Count());
        Assert.All(await db.SeatLocks.ToListAsync(), item =>
        {
            Assert.Equal("ACTIVE", item.LockStatus);
            Assert.Equal(7, item.UserId);
            Assert.Equal("alice", item.CreateBy);
        });
    }

    [Fact]
    public async Task LockAsync_RejectsDuplicateSeatIds()
    {
        await using var db = CreateDbContext();
        await SeedSellableSessionAsync(db);
        var service = new SeatLockService(db, new FixedTimeProvider(Now));

        var result = await service.LockAsync(
            7,
            "alice",
            10,
            new SeatLockBatchRequest([50, 50]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SEAT_LOCK_INVALID_REQUEST", result.ErrorCode);
        Assert.Empty(await db.SeatLocks.ToListAsync());
    }

    [Fact]
    public async Task LockAsync_RejectsMoreThanNineHundredNinetyNineSeats()
    {
        await using var db = CreateDbContext();
        var service = new SeatLockService(db, new FixedTimeProvider(Now));
        var seatIds = Enumerable.Range(1, 1_000)
            .Select(value => (long)value)
            .ToArray();

        var result = await service.LockAsync(
            7,
            "alice",
            10,
            new SeatLockBatchRequest(seatIds),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SEAT_LOCK_INVALID_REQUEST", result.ErrorCode);
    }

    [Fact]
    public async Task LockAsync_RejectsMissingSession()
    {
        await using var db = CreateDbContext();
        var service = new SeatLockService(db, new FixedTimeProvider(Now));

        var result = await service.LockAsync(
            7,
            "alice",
            999,
            new SeatLockBatchRequest([50]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SEAT_LOCK_SESSION_NOT_FOUND", result.ErrorCode);
        Assert.Empty(await db.SeatLocks.ToListAsync());
    }

    [Fact]
    public async Task LockAsync_RejectsSessionOutsideSaleWindow()
    {
        await using var db = CreateDbContext();
        await SeedSellableSessionAsync(db);
        (await db.ShowSessions.FindAsync(10L))!.SaleStartTime = Now.UtcDateTime.AddHours(1);
        await db.SaveChangesAsync();
        var service = new SeatLockService(db, new FixedTimeProvider(Now));

        var result = await service.LockAsync(
            7,
            "alice",
            10,
            new SeatLockBatchRequest([50]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SEAT_LOCK_SESSION_UNAVAILABLE", result.ErrorCode);
        Assert.Empty(await db.SeatLocks.ToListAsync());
    }

    [Fact]
    public async Task LockAsync_RejectsSeatFromAnotherSeatMap()
    {
        await using var db = CreateDbContext();
        await SeedSellableSessionAsync(db);
        db.AddRange(
            new SeatSection
            {
                SeatSectionId = 41,
                SeatMapId = 31,
                SectionCode = "B",
                SectionName = "B区",
                IsSellable = true
            },
            new Seat
            {
                SeatId = 52,
                SeatSectionId = 41,
                RowCode = "1",
                SeatNo = "1",
                IsSellable = true,
                SeatStatus = "ENABLED"
            });
        await db.SaveChangesAsync();
        var service = new SeatLockService(db, new FixedTimeProvider(Now));

        var result = await service.LockAsync(
            7,
            "alice",
            10,
            new SeatLockBatchRequest([52]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SEAT_LOCK_SEAT_NOT_FOUND", result.ErrorCode);
        Assert.Empty(await db.SeatLocks.ToListAsync());
    }

    [Fact]
    public async Task LockAsync_RejectsSeatInUnsellableSection()
    {
        await using var db = CreateDbContext();
        await SeedSellableSessionAsync(db);
        (await db.SeatSections.FindAsync(40L))!.IsSellable = false;
        await db.SaveChangesAsync();
        var service = new SeatLockService(db, new FixedTimeProvider(Now));

        var result = await service.LockAsync(
            7,
            "alice",
            10,
            new SeatLockBatchRequest([50]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SEAT_LOCK_SEAT_UNAVAILABLE", result.ErrorCode);
        Assert.Empty(await db.SeatLocks.ToListAsync());
    }

    [Fact]
    public async Task LockAsync_RejectsWholeBatchWhenOneSeatIsAlreadyLocked()
    {
        await using var db = CreateDbContext();
        await SeedSellableSessionAsync(db);
        db.Add(CreateActiveLock(70, 51, 8, "existing-lock"));
        await db.SaveChangesAsync();
        var service = new SeatLockService(db, new FixedTimeProvider(Now));

        var result = await service.LockAsync(
            7,
            "alice",
            10,
            new SeatLockBatchRequest([50, 51]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SEAT_LOCK_CONFLICT", result.ErrorCode);
        Assert.DoesNotContain(
            await db.SeatLocks.ToListAsync(),
            item => item.SeatId == 50);
    }

    [Fact]
    public async Task LockAsync_RejectsActivelyReservedSeat()
    {
        await using var db = CreateDbContext();
        await SeedSellableSessionAsync(db);
        db.Add(new SeatReservation
        {
            SeatReservationId = 80,
            SessionId = 10,
            SeatId = 50,
            OrderItemId = 90,
            ReservationType = "ORDER",
            ReservationStatus = "ACTIVE",
            ReserveTime = Now.UtcDateTime.AddMinutes(-1)
        });
        await db.SaveChangesAsync();
        var service = new SeatLockService(db, new FixedTimeProvider(Now));

        var result = await service.LockAsync(
            7,
            "alice",
            10,
            new SeatLockBatchRequest([50]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SEAT_LOCK_CONFLICT", result.ErrorCode);
        Assert.Empty(await db.SeatLocks.ToListAsync());
    }

    [Fact]
    public async Task LockAsync_ExpiresOldLockAndCreatesReplacement()
    {
        await using var db = CreateDbContext();
        await SeedSellableSessionAsync(db);
        db.Add(new SeatLock
        {
            SeatLockId = 70,
            SessionId = 10,
            SeatId = 50,
            UserId = 8,
            LockToken = "expired-lock",
            LockStatus = "ACTIVE",
            LockTime = Now.UtcDateTime.AddMinutes(-20),
            ExpireTime = Now.UtcDateTime.AddMinutes(-10)
        });
        await db.SaveChangesAsync();
        var service = new SeatLockService(db, new FixedTimeProvider(Now));

        var result = await service.LockAsync(
            7,
            "alice",
            10,
            new SeatLockBatchRequest([50]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("EXPIRED", (await db.SeatLocks.FindAsync(70L))!.LockStatus);
        Assert.Contains(
            await db.SeatLocks.ToListAsync(),
            item => item.SeatId == 50 &&
                    item.UserId == 7 &&
                    item.LockStatus == "ACTIVE");
    }

    [Fact]
    public async Task ReleaseAsync_ReleasesCurrentUsersCompleteBatch()
    {
        await using var db = CreateDbContext();
        db.AddRange(
            CreateActiveLock(70, 50, 7, "token-50"),
            CreateActiveLock(71, 51, 7, "token-51"));
        await db.SaveChangesAsync();
        var service = new SeatLockService(db, new FixedTimeProvider(Now));

        var result = await service.ReleaseAsync(
            7,
            "alice",
            10,
            new SeatLockReleaseRequest(["token-50", "token-51"]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.ReleasedCount);
        Assert.All(await db.SeatLocks.ToListAsync(), item =>
        {
            Assert.Equal("RELEASED", item.LockStatus);
            Assert.Equal(Now.UtcDateTime, item.ReleaseTime);
            Assert.Equal("alice", item.UpdateBy);
        });
    }

    [Fact]
    public async Task ReleaseAsync_DoesNotPartiallyReleaseWhenOneTokenIsInvalid()
    {
        await using var db = CreateDbContext();
        db.Add(CreateActiveLock(70, 50, 7, "token-50"));
        await db.SaveChangesAsync();
        var service = new SeatLockService(db, new FixedTimeProvider(Now));

        var result = await service.ReleaseAsync(
            7,
            "alice",
            10,
            new SeatLockReleaseRequest(["token-50", "missing-token"]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SEAT_LOCK_NOT_FOUND", result.ErrorCode);
        Assert.Equal("ACTIVE", (await db.SeatLocks.FindAsync(70L))!.LockStatus);
    }

    [Fact]
    public async Task ReleaseAsync_RejectsMoreThanNineHundredNinetyNineTokens()
    {
        await using var db = CreateDbContext();
        var service = new SeatLockService(db, new FixedTimeProvider(Now));
        var tokens = Enumerable.Range(1, 1_000)
            .Select(value => $"token-{value}")
            .ToArray();

        var result = await service.ReleaseAsync(
            7,
            "alice",
            10,
            new SeatLockReleaseRequest(tokens),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SEAT_LOCK_INVALID_REQUEST", result.ErrorCode);
    }

    [Fact]
    public async Task ReleaseAsync_DoesNotReleaseAnotherUsersLock()
    {
        await using var db = CreateDbContext();
        db.Add(CreateActiveLock(70, 50, 8, "bob-token"));
        await db.SaveChangesAsync();
        var service = new SeatLockService(db, new FixedTimeProvider(Now));

        var result = await service.ReleaseAsync(
            7,
            "alice",
            10,
            new SeatLockReleaseRequest(["bob-token"]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SEAT_LOCK_NOT_FOUND", result.ErrorCode);
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

    private static SeatLock CreateActiveLock(
        long id,
        long seatId,
        long userId,
        string token) => new()
        {
            SeatLockId = id,
            SessionId = 10,
            SeatId = seatId,
            UserId = userId,
            LockToken = token,
            LockStatus = "ACTIVE",
            LockTime = Now.UtcDateTime.AddMinutes(-1),
            ExpireTime = Now.UtcDateTime.AddMinutes(9)
        };

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
