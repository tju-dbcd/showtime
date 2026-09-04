using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Entities.SeatZone;
using ShowtimeBackend.Entities.ShowSession;
using ShowtimeBackend.Services.OrderTicket;
using ShowtimeBackend.Services.SeatZone;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class OrderIdempotencyConcurrencyTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SameKeyAndRequest_TwoRelationalCallsReturnTheSameSingleOrder()
    {
        await using var database = await SharedDatabase.CreateAsync();
        await database.AddLockAsync(50, "lock-50");
        var pause = new PauseAfterIdempotencyReadInterceptor();
        await using var loserConnection = await database.OpenConnectionAsync();
        await using var winnerConnection = await database.OpenConnectionAsync();
        await using var loserDb = CreateDbContext(loserConnection, pause);
        await using var winnerDb = CreateDbContext(winnerConnection);
        var guard = new ConcurrentSeatLockGuard();
        var loser = CreateService(loserDb, guard);
        var winner = CreateService(winnerDb, guard);
        var request = Request(50, 60, "lock-50");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var loserTask = loser.CreateAsync(
            7, "loser", "same-key", request, timeout.Token);
        await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var winnerResult = await winner.CreateAsync(
            7, "winner", "same-key", request, CancellationToken.None);
        pause.Release.TrySetResult();
        var loserResult = await loserTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(winnerResult.IsSuccess, winnerResult.Message);
        Assert.True(loserResult.IsSuccess, loserResult.Message);
        Assert.Equal(winnerResult.Value!.OrderId, loserResult.Value!.OrderId);
        await using var verificationConnection = await database.OpenConnectionAsync();
        await using var verification = CreateDbContext(verificationConnection);
        Assert.Equal(1, await verification.Set<Order>().CountAsync());
        Assert.Equal(1, await verification.Set<OrderItem>().CountAsync());
        Assert.Equal(1, await verification.SeatReservations.CountAsync());
        Assert.Single(guard.ReleaseCalls);
    }

    [Fact]
    public async Task SameKeyDifferentSeats_LoserConflictsAndItsLockConversionRollsBack()
    {
        await using var database = await SharedDatabase.CreateAsync();
        await database.AddLockAsync(50, "lock-50");
        await database.AddLockAsync(51, "lock-51");
        var pause = new PauseAfterIdempotencyReadInterceptor();
        await using var loserConnection = await database.OpenConnectionAsync();
        await using var winnerConnection = await database.OpenConnectionAsync();
        await using var loserDb = CreateDbContext(loserConnection, pause);
        await using var winnerDb = CreateDbContext(winnerConnection);
        var guard = new ConcurrentSeatLockGuard();
        var loser = CreateService(loserDb, guard);
        var winner = CreateService(winnerDb, guard);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var loserTask = loser.CreateAsync(
            7,
            "loser",
            "shared-key",
            Request(51, 61, "lock-51"),
            timeout.Token);
        await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var winnerResult = await winner.CreateAsync(
            7,
            "winner",
            "shared-key",
            Request(50, 60, "lock-50"),
            CancellationToken.None);
        pause.Release.TrySetResult();
        var loserResult = await loserTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(winnerResult.IsSuccess, winnerResult.Message);
        Assert.False(loserResult.IsSuccess);
        Assert.Equal("ORDER_IDEMPOTENCY_CONFLICT", loserResult.ErrorCode);
        await using var verificationConnection = await database.OpenConnectionAsync();
        await using var verification = CreateDbContext(verificationConnection);
        Assert.Equal(1, await verification.Set<Order>().CountAsync());
        var locks = await verification.SeatLocks.AsNoTracking()
            .OrderBy(item => item.SeatId)
            .ToListAsync();
        Assert.Equal("CONVERTED", locks[0].LockStatus);
        Assert.Equal("ACTIVE", locks[1].LockStatus);
        Assert.Single(guard.ReleaseCalls);
        Assert.Equal(50, guard.ReleaseCalls.Single().SeatId);
    }

    [Fact]
    public async Task IdempotencyUniqueFailureWithoutWinner_ReturnsInternalFailureAndRollsBack()
    {
        await using var database = await SharedDatabase.CreateAsync();
        await database.AddLockAsync(50, "lock-50");
        await using var connection = await database.OpenConnectionAsync();
        await using var db = CreateDbContext(
            connection,
            new FailFirstOrderSaveInterceptor());
        var guard = new ConcurrentSeatLockGuard();
        var service = CreateService(db, guard);

        var result = await service.CreateAsync(
            7,
            "creator",
            "missing-winner",
            Request(50, 60, "lock-50"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderTicketFailure.Internal, result.Failure);
        Assert.Equal("ORDER_IDEMPOTENCY_RECOVERY_FAILED", result.ErrorCode);
        await using var verificationConnection = await database.OpenConnectionAsync();
        await using var verification = CreateDbContext(verificationConnection);
        Assert.Empty(await verification.Set<Order>().ToListAsync());
        Assert.Equal(
            "ACTIVE",
            (await verification.SeatLocks.SingleAsync()).LockStatus);
        Assert.Empty(guard.ReleaseCalls);
    }

    private static CreateOrderRequest Request(long seatId, long strategyId, string token) =>
        new(10, [new CreateOrderItemRequest(seatId, strategyId, null, token)], null);

    private static OrderService CreateService(
        AppDbContext db,
        ISeatLockGuard guard) => new(db, new FixedTimeProvider(Now), guard);

    private static AppDbContext CreateDbContext(
        SqliteConnection connection,
        params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<SqliteAuthDbContext>()
            .UseSqlite(connection);
        if (interceptors.Length > 0)
            options.AddInterceptors(interceptors);
        return new SqliteAuthDbContext(options.Options);
    }

    private sealed class SharedDatabase(string path) : IAsyncDisposable
    {
        private readonly string connectionString =
            $"Data Source={path};Pooling=False;Foreign Keys=False;Default Timeout=5";

        public static async Task<SharedDatabase> CreateAsync()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                $"showtime-order-idempotency-{Guid.NewGuid():N}.db");
            var database = new SharedDatabase(path);
            await using var connection = await database.OpenConnectionAsync();
            await using var db = CreateDbContext(connection);
            await db.Database.EnsureCreatedAsync();
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
            await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=OFF;");
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
                CreateSeat(50, 1),
                CreateSeat(51, 2),
                CreateStrategy(60, 100m),
                CreateStrategy(61, 120m));
            await db.SaveChangesAsync();
            return database;
        }

        public async Task AddLockAsync(long seatId, string token)
        {
            await using var connection = await OpenConnectionAsync();
            await using var db = CreateDbContext(connection);
            db.Add(new SeatLock
            {
                SessionId = 10,
                SeatId = seatId,
                UserId = 7,
                LockToken = token,
                LockStatus = "ACTIVE",
                LockTime = Now.UtcDateTime.AddMinutes(-1),
                ExpireTime = Now.UtcDateTime.AddMinutes(9),
            });
            await db.SaveChangesAsync();
        }

        public async Task<SqliteConnection> OpenConnectionAsync()
        {
            var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA busy_timeout=5000;";
            await command.ExecuteNonQueryAsync();
            return connection;
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path))
                File.Delete(path);
            var walPath = $"{path}-wal";
            if (File.Exists(walPath))
                File.Delete(walPath);
            var sharedMemoryPath = $"{path}-shm";
            if (File.Exists(sharedMemoryPath))
                File.Delete(sharedMemoryPath);
            return ValueTask.CompletedTask;
        }

        private static Seat CreateSeat(long seatId, int colIndex) => new()
        {
            SeatId = seatId,
            SeatSectionId = 40,
            RowCode = "1",
            SeatNo = colIndex.ToString(),
            RowIndex = 1,
            ColIndex = colIndex,
            IsSellable = true,
            SeatStatus = "ENABLED",
        };

        private static PriceStrategy CreateStrategy(long strategyId, decimal price) => new()
        {
            PriceStrategyId = strategyId,
            SessionId = 10,
            SeatSectionId = 40,
            Price = price,
            Status = "ENABLED",
        };
    }

    private sealed class PauseAfterIdempotencyReadInterceptor : DbCommandInterceptor
    {
        private int paused;

        public TaskCompletionSource Reached { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref paused, 1) == 0 &&
                command.CommandText.Contains("IDEMPOTENCY_KEY", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains("T_ORDER", StringComparison.OrdinalIgnoreCase))
            {
                Reached.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }

            return result;
        }
    }

    private sealed class FailFirstOrderSaveInterceptor : SaveChangesInterceptor
    {
        private int failed;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref failed, 1) == 0)
            {
                throw new DbUpdateException(
                    "Simulated idempotency constraint race.",
                    new InvalidOperationException(
                        "SQLite Error 19: UNIQUE constraint failed: " +
                        "T_ORDER.USER_ID, T_ORDER.IDEMPOTENCY_KEY"));
            }

            return base.SavingChangesAsync(
                eventData,
                result,
                cancellationToken);
        }
    }

    private sealed class ConcurrentSeatLockGuard : ISeatLockGuard
    {
        public ConcurrentQueue<(long SessionId, long SeatId, string Token)> ReleaseCalls { get; } = [];

        public Task<SeatLockGuardAcquireResult> TryAcquireAsync(
            long sessionId,
            IReadOnlyCollection<SeatLock> locks,
            TimeSpan ttl,
            CancellationToken cancellationToken) =>
            Task.FromResult(SeatLockGuardAcquireResult.Acquired);

        public Task ReleaseAsync(long sessionId, long seatId, string token)
        {
            ReleaseCalls.Enqueue((sessionId, seatId, token));
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
