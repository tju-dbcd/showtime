using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Common;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Entities.SeatZone;
using ShowtimeBackend.Entities.ShowSession;
using ShowtimeBackend.Services.OrderTicket;
using ShowtimeBackend.Services.SeatZone;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class OrderIdempotencyServiceTests
{
    [Fact]
    public async Task CreateAsync_FirstRequestPersistsNormalizedKeyAndSha256Hash()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddLockAsync(7, 50, "lock-50");

        var result = await fixture.Service.CreateAsync(
            7,
            "alice",
            "  Case-Sensitive-Key  ",
            Request([new(50, 60, null, " lock-50 ")], "  remark  "),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Message);
        var order = await fixture.Db.Set<Order>().AsNoTracking().SingleAsync();
        Assert.Equal("Case-Sensitive-Key", order.IdempotencyKey);
        Assert.Matches("^[0-9A-F]{64}$", order.IdempotencyRequestHash!);
        Assert.Equal("remark", order.Remark);
    }

    [Fact]
    public async Task CreateAsync_ReorderedReplayReturnsSameOrderWithoutRepeatingWritesOrGuardRelease()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddLockAsync(7, 50, "lock-50");
        await fixture.AddLockAsync(7, 51, "lock-51");
        var firstRequest = Request(
            [new(50, 60, null, "lock-50"), new(51, 61, null, "lock-51")],
            "note");
        var replayRequest = Request(
            [new(51, 61, null, "lock-51"), new(50, 60, null, "lock-50")],
            " note ");

        var first = await fixture.Service.CreateAsync(
            7, "alice", "replay-key", firstRequest, CancellationToken.None);
        var replay = await fixture.Service.CreateAsync(
            7, "alice", " replay-key ", replayRequest, CancellationToken.None);

        Assert.True(first.IsSuccess, first.Message);
        Assert.True(replay.IsSuccess, replay.Message);
        Assert.Equal(first.Value!.OrderId, replay.Value!.OrderId);
        Assert.Equal(first.Value.OrderNo, replay.Value.OrderNo);
        Assert.Equal(1, await fixture.Db.Set<Order>().CountAsync());
        Assert.Equal(2, await fixture.Db.Set<OrderItem>().CountAsync());
        Assert.Equal(2, await fixture.Db.SeatReservations.CountAsync());
        Assert.All(await fixture.Db.SeatLocks.AsNoTracking().ToListAsync(), item =>
            Assert.Equal("CONVERTED", item.LockStatus));
        Assert.Equal(2, fixture.Guard.ReleaseCalls.Count);
    }

    [Theory]
    [InlineData("session")]
    [InlineData("seat")]
    [InlineData("price")]
    [InlineData("real-name")]
    [InlineData("lock-token")]
    [InlineData("remark")]
    public async Task CreateAsync_SameKeyWithChangedBusinessInputReturnsConflict(string mutation)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddLockAsync(7, 50, "lock-50");
        var original = Request([new(50, 60, null, "lock-50")], "note");
        var first = await fixture.Service.CreateAsync(
            7, "alice", "conflict-key", original, CancellationToken.None);
        Assert.True(first.IsSuccess, first.Message);
        var changed = mutation switch
        {
            "session" => original with { SessionId = 11 },
            "seat" => Request([new(51, 60, null, "lock-50")], "note"),
            "price" => Request([new(50, 61, null, "lock-50")], "note"),
            "real-name" => Request([new(50, 60, 70, "lock-50")], "note"),
            "lock-token" => Request([new(50, 60, null, "other-lock")], "note"),
            "remark" => original with { Remark = "other-note" },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation)),
        };

        var replay = await fixture.Service.CreateAsync(
            7, "alice", "conflict-key", changed, CancellationToken.None);

        Assert.False(replay.IsSuccess);
        Assert.Equal(OrderTicketFailure.Conflict, replay.Failure);
        Assert.Equal("ORDER_IDEMPOTENCY_CONFLICT", replay.ErrorCode);
        Assert.Equal(1, await fixture.Db.Set<Order>().CountAsync());
        Assert.Single(fixture.Guard.ReleaseCalls);
    }

    [Fact]
    public async Task CreateAsync_SameKeyForDifferentUsersCreatesIndependentOrders()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddLockAsync(7, 50, "lock-user-7");
        await fixture.AddLockAsync(8, 51, "lock-user-8");

        var first = await fixture.Service.CreateAsync(
            7,
            "alice",
            "shared-key",
            Request([new(50, 60, null, "lock-user-7")], null),
            CancellationToken.None);
        var second = await fixture.Service.CreateAsync(
            8,
            "bob",
            "shared-key",
            Request([new(51, 61, null, "lock-user-8")], null),
            CancellationToken.None);

        Assert.True(first.IsSuccess, first.Message);
        Assert.True(second.IsSuccess, second.Message);
        Assert.NotEqual(first.Value!.OrderId, second.Value!.OrderId);
        Assert.Equal(2, await fixture.Db.Set<Order>().CountAsync());
    }

    [Fact]
    public async Task CreateAsync_DifferentKeyCannotRepurchaseConvertedSeat()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddLockAsync(7, 50, "lock-50");
        var request = Request([new(50, 60, null, "lock-50")], null);
        var first = await fixture.Service.CreateAsync(
            7, "alice", "first-key", request, CancellationToken.None);

        var second = await fixture.Service.CreateAsync(
            7, "alice", "different-key", request, CancellationToken.None);

        Assert.True(first.IsSuccess, first.Message);
        Assert.False(second.IsSuccess);
        Assert.Equal("ORDER_SEAT_LOCK_INVALID", second.ErrorCode);
        Assert.Equal(1, await fixture.Db.Set<Order>().CountAsync());
        Assert.Single(fixture.Guard.ReleaseCalls);
    }

    private static CreateOrderRequest Request(
        IReadOnlyList<CreateOrderItemRequest> items,
        string? remark) => new(10, items, remark);

    private sealed class Fixture(
        SqliteConnection connection,
        AppDbContext db,
        FakeSeatLockGuard guard) : IAsyncDisposable
    {
        private static readonly DateTimeOffset Now =
            new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

        public AppDbContext Db { get; } = db;
        public FakeSeatLockGuard Guard { get; } = guard;
        public OrderService Service { get; } = new(db, new FixedTimeProvider(Now), guard);

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<SqliteAuthDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new SqliteAuthDbContext(options);
            await db.Database.EnsureCreatedAsync();
            await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
            db.AddRange(
                new ShowSession
                {
                    SessionId = 10,
                    ShowId = 20,
                    SeatMapId = 30,
                    StartTime = Now.UtcDateTime.AddDays(10),
                    EndTime = Now.UtcDateTime.AddDays(10).AddHours(2),
                    SaleStartTime = Now.UtcDateTime.AddDays(-10),
                    SaleEndTime = Now.UtcDateTime.AddDays(9),
                },
                new SeatSection
                {
                    SeatSectionId = 40,
                    SeatMapId = 30,
                    SectionCode = "A",
                    SectionName = "A区",
                },
                new Seat
                {
                    SeatId = 50,
                    SeatSectionId = 40,
                    RowCode = "1",
                    SeatNo = "1",
                    RowIndex = 1,
                    ColIndex = 1,
                    IsSellable = true,
                    SeatStatus = "ENABLED",
                },
                new Seat
                {
                    SeatId = 51,
                    SeatSectionId = 40,
                    RowCode = "1",
                    SeatNo = "2",
                    RowIndex = 1,
                    ColIndex = 2,
                    IsSellable = true,
                    SeatStatus = "ENABLED",
                },
                new PriceStrategy
                {
                    PriceStrategyId = 60,
                    SessionId = 10,
                    SeatSectionId = 40,
                    Price = 100m,
                    Status = "ENABLED",
                },
                new PriceStrategy
                {
                    PriceStrategyId = 61,
                    SessionId = 10,
                    SeatSectionId = 40,
                    Price = 120m,
                    Status = "ENABLED",
                });
            await db.SaveChangesAsync();
            return new Fixture(connection, db, new FakeSeatLockGuard());
        }

        public async Task AddLockAsync(long userId, long seatId, string token)
        {
            Db.Add(new SeatLock
            {
                SessionId = 10,
                SeatId = seatId,
                UserId = userId,
                LockToken = token,
                LockStatus = "ACTIVE",
                LockTime = Now.UtcDateTime.AddMinutes(-1),
                ExpireTime = Now.UtcDateTime.AddMinutes(9),
            });
            await Db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class FakeSeatLockGuard : ISeatLockGuard
    {
        public List<(long SessionId, long SeatId, string Token)> ReleaseCalls { get; } = [];

        public Task<SeatLockGuardAcquireResult> TryAcquireAsync(
            long sessionId,
            IReadOnlyCollection<SeatLock> locks,
            TimeSpan ttl,
            CancellationToken cancellationToken) =>
            Task.FromResult(SeatLockGuardAcquireResult.Acquired);

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
