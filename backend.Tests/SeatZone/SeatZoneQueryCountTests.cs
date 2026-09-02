using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ShowtimeBackend.Common;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.SeatZone;
using ShowtimeBackend.Entities.SeatZone;
using ShowtimeBackend.Entities.ShowSession;
using ShowtimeBackend.Services.SeatZone;

namespace ShowtimeBackend.Tests.SeatZone;

public sealed class SeatZoneQueryCountTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SessionSeatMap_ReadQueryCount_DoesNotGrowWithSeatCount()
    {
        var small = await ReadSessionSeatMapAsync(4);
        var large = await ReadSessionSeatMapAsync(200);

        Assert.True(small.Result.IsSuccess);
        Assert.True(large.Result.IsSuccess);
        Assert.Equal(4, GetSeatCount(small.Result));
        Assert.Equal(200, GetSeatCount(large.Result));
        Assert.Equal(small.ReadCommandCount, large.ReadCommandCount);
        Assert.Equal(6, small.ReadCommandCount);
        Assert.Equal("LOCKED", GetAvailability(small.Result, 1));
        Assert.Equal("RESERVED", GetAvailability(small.Result, 2));
        Assert.Equal("AVAILABLE", GetAvailability(small.Result, 3));
        Assert.Equal("UNAVAILABLE", GetAvailability(small.Result, 4));
        Assert.Contains(large.Result.Data!.SeatMap.Sections, section =>
            section.SectionCode == "EMPTY" && section.Seats.Count == 0);
    }

    [Fact]
    public async Task LockSeats_ReadQueryCount_DoesNotGrowWithBatchSize()
    {
        var small = await LockSeatsAsync(1);
        var large = await LockSeatsAsync(100);

        Assert.True(small.Result.IsSuccess);
        Assert.True(large.Result.IsSuccess);
        AssertLockSemantics(small, SeatIds(1));
        AssertLockSemantics(large, SeatIds(100));
        Assert.Equal(small.ReadCommandCount, large.ReadCommandCount);
        Assert.Equal(4, small.ReadCommandCount);
    }

    [Fact]
    public async Task ListSeats_ReadQueryCount_DoesNotGrowWithSectionSize()
    {
        var small = await ListSeatsAsync(4);
        var large = await ListSeatsAsync(200);

        Assert.True(small.Result.IsSuccess);
        Assert.True(large.Result.IsSuccess);
        Assert.Equal(4, small.Result.Data!.TotalCount);
        Assert.Equal(4, small.Result.Data.Items.Count);
        Assert.Equal(200, large.Result.Data!.TotalCount);
        Assert.Equal(10, large.Result.Data.Items.Count);
        Assert.Equal(small.ReadCommandCount, large.ReadCommandCount);
        Assert.Equal(3, small.ReadCommandCount);
    }

    [Fact]
    public async Task UpdateSeats_ReadQueryCount_DoesNotGrowWithBatchSize()
    {
        var small = await UpdateSeatsAsync(1);
        var large = await UpdateSeatsAsync(100);

        Assert.True(small.Result.IsSuccess);
        Assert.True(large.Result.IsSuccess);
        AssertUpdateSemantics(small, SeatIds(1));
        AssertUpdateSemantics(large, SeatIds(100));
        Assert.Equal(small.ReadCommandCount, large.ReadCommandCount);
        Assert.Equal(3, small.ReadCommandCount);
        Assert.Equal(small.UpdateCommandCount, large.UpdateCommandCount);
    }

    [Fact]
    public async Task UpdateSeats_HandlesChunkBoundaryWithSinglePersistenceCommand()
    {
        var execution = await UpdateSeatsAsync(999);

        Assert.True(execution.Result.IsSuccess);
        AssertUpdateSemantics(execution, SeatIds(999));
        Assert.Equal(999, execution.Result.Data!.UpdatedCount);
        Assert.Equal(1, execution.UpdateCommandCount);
    }

    [Fact]
    public async Task UpdateSeats_RollsBackWhenExecuteUpdateFails()
    {
        await using var database = await QueryCountDatabase.CreateAsync(new ThrowingUpdateInterceptor());
        await database.SeedSeatSectionAsync(2);

        await Assert.ThrowsAsync<InvalidOperationException>(() => new SeatAdminService(database.Db).UpdateSeatsAsync(
            40,
            new SeatBatchUpdateRequest(SeatIds(2), null, "DISABLED", null, null),
            CancellationToken.None));

        database.Db.ChangeTracker.Clear();
        var statuses = await database.Db.Seats.AsNoTracking()
            .Where(seat => seat.SeatSectionId == 40)
            .Select(seat => seat.SeatStatus)
            .ToListAsync();

        Assert.Equal(2, statuses.Count);
        Assert.All(statuses, status => Assert.Equal("ENABLED", status));
    }

    [Fact]
    public async Task LockSeats_UsesMultiplePersistenceBatchesBeyondChunkBoundary()
    {
        await using var database = await QueryCountDatabase.CreateAsync();
        await database.SeedSellableSessionAsync(101);
        database.Counter.Reset();
        database.Db.ResetPersistenceCounter();

        var result = await new SeatLockService(
                database.Db,
                new FixedTimeProvider(Now),
                TimeSpan.FromMinutes(10),
                seatLockGuard: null,
                guardEnabled: false)
            .LockAsync(7, "query-count-test", 10, new SeatLockBatchRequest(SeatIds(101)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(101, result.Value!.Locks.Count);
        Assert.True(database.Db.SaveChangesCallCount >= 2);
    }

    [Fact]
    public async Task LockSeats_UsesNativeBatchWriterAndPreservesRequestOrder()
    {
        await using var database = await QueryCountDatabase.CreateAsync();
        await database.SeedSellableSessionAsync(501);
        var writer = new RecordingSeatLockBatchWriter();
        var requestedSeatIds = SeatIds(501).Reverse().ToArray();

        var result = await new SeatLockService(
                database.Db,
                new FixedTimeProvider(Now),
                TimeSpan.FromMinutes(10),
                seatLockGuard: null,
                guardEnabled: false,
                seatLockBatchWriter: writer)
            .LockAsync(7, "query-count-test", 10,
                new SeatLockBatchRequest(requestedSeatIds), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(requestedSeatIds, result.Value!.Locks.Select(item => item.SeatId));
        Assert.Equal(1, writer.CallCount);
        Assert.Equal(501, writer.ReceivedCount);
        Assert.All(writer.ReceivedStates, state => Assert.Equal(EntityState.Detached, state));
    }

    [Fact]
    public async Task LockSeats_RollsBackAndReleasesGuardWhenLaterPersistenceBatchFails()
    {
        await using var database = await QueryCountDatabase.CreateAsync();
        await database.SeedSellableSessionAsync(101);
        database.Db.ResetPersistenceCounter();
        database.Db.ThrowOnSaveChangesCall = 2;
        var guard = new RecordingSeatLockGuard();

        await Assert.ThrowsAsync<InvalidOperationException>(() => new SeatLockService(
                database.Db,
                new FixedTimeProvider(Now),
                TimeSpan.FromMinutes(10),
                guard,
                guardEnabled: true)
            .LockAsync(7, "query-count-test", 10, new SeatLockBatchRequest(SeatIds(101)), CancellationToken.None));

        database.Db.ChangeTracker.Clear();
        Assert.Empty(await database.Db.SeatLocks.AsNoTracking().ToListAsync());
        Assert.Equal(101, guard.ReleaseCalls.Count);
    }

    private static async Task<(ServiceResult<SessionSeatMapDto> Result, int ReadCommandCount)> ReadSessionSeatMapAsync(int seatCount)
    {
        await using var database = await QueryCountDatabase.CreateAsync();
        await database.SeedSessionMapAsync(seatCount);
        database.Counter.Reset();

        var result = await new SessionSeatMapQueryService(database.Db, new FixedTimeProvider(Now))
            .GetAsync(10, CancellationToken.None);

        return (result, database.Counter.ReadCommandCount);
    }

    private static async Task<(SeatZoneResult<SeatLockBatchResponse> Result, int ReadCommandCount, IReadOnlyList<SeatLock> ActiveLocks)> LockSeatsAsync(int seatCount)
    {
        await using var database = await QueryCountDatabase.CreateAsync();
        await database.SeedSellableSessionAsync(seatCount);
        database.Counter.Reset();

        var result = await new SeatLockService(
                database.Db,
                new FixedTimeProvider(Now),
                TimeSpan.FromMinutes(10),
                seatLockGuard: null,
                guardEnabled: false)
            .LockAsync(7, "query-count-test", 10, new SeatLockBatchRequest(SeatIds(seatCount)), CancellationToken.None);

        var readCommandCount = database.Counter.ReadCommandCount;
        var activeLocks = await database.Db.SeatLocks.AsNoTracking()
            .Where(item => item.SessionId == 10 && item.LockStatus == "ACTIVE")
            .OrderBy(item => item.SeatId)
            .ToListAsync();

        return (result, readCommandCount, activeLocks);
    }

    private static async Task<(ServiceResult<PagedResponse<SeatResponse>> Result, int ReadCommandCount)> ListSeatsAsync(int seatCount)
    {
        await using var database = await QueryCountDatabase.CreateAsync();
        await database.SeedSeatSectionAsync(seatCount);
        database.Counter.Reset();

        var result = await new SeatAdminService(database.Db).ListSeatsAsync(
            40,
            new SeatListQuery(null, null, null, null, 1, 10),
            CancellationToken.None);

        return (result, database.Counter.ReadCommandCount);
    }

    private static async Task<(ServiceResult<SeatBatchUpdateResponse> Result, int ReadCommandCount, int UpdateCommandCount, IReadOnlyList<Seat> UpdatedSeats)> UpdateSeatsAsync(int seatCount)
    {
        await using var database = await QueryCountDatabase.CreateAsync();
        await database.SeedSeatSectionAsync(seatCount);
        database.Counter.Reset();

        var result = await new SeatAdminService(database.Db).UpdateSeatsAsync(
            40,
            new SeatBatchUpdateRequest(SeatIds(seatCount), null, "DISABLED", null, null),
            CancellationToken.None);

        var readCommandCount = database.Counter.ReadCommandCount;
        var updateCommandCount = database.Counter.UpdateCommandCount;
        var updatedSeats = await database.Db.Seats.AsNoTracking()
            .Where(item => item.SeatSectionId == 40 && SeatIds(seatCount).Contains(item.SeatId))
            .OrderBy(item => item.SeatId)
            .ToListAsync();

        return (result, readCommandCount, updateCommandCount, updatedSeats);
    }

    private static IReadOnlyList<long> SeatIds(int count) =>
        Enumerable.Range(1, count).Select(index => (long)index).ToArray();

    private static int GetSeatCount(ServiceResult<SessionSeatMapDto> result) =>
        result.Data!.SeatMap.Sections.Sum(section => section.Seats.Count);

    private static string GetAvailability(ServiceResult<SessionSeatMapDto> result, long seatId) =>
        result.Data!.SeatMap.Sections.SelectMany(section => section.Seats)
            .Single(seat => seat.SeatId == seatId).AvailabilityStatus;

    private static void AssertLockSemantics(
        (SeatZoneResult<SeatLockBatchResponse> Result, int ReadCommandCount, IReadOnlyList<SeatLock> ActiveLocks) execution,
        IReadOnlyList<long> requestedSeatIds)
    {
        var response = execution.Result.Value!;
        var expectedExpireTime = Now.UtcDateTime.AddMinutes(10);

        Assert.Equal(10, response.SessionId);
        Assert.Equal(expectedExpireTime, response.ExpireTime);
        Assert.Equal(requestedSeatIds.Order(), response.Locks.Select(item => item.SeatId).Order());
        Assert.All(response.Locks, item => Assert.Equal(expectedExpireTime, item.ExpireTime));
        Assert.Equal(requestedSeatIds.Count, execution.ActiveLocks.Count);
        Assert.Equal(requestedSeatIds.Order(), execution.ActiveLocks.Select(item => item.SeatId));
        Assert.All(execution.ActiveLocks, item => Assert.Equal("ACTIVE", item.LockStatus));
    }

    private static void AssertUpdateSemantics(
        (ServiceResult<SeatBatchUpdateResponse> Result, int ReadCommandCount, int UpdateCommandCount, IReadOnlyList<Seat> UpdatedSeats) execution,
        IReadOnlyList<long> requestedSeatIds)
    {
        var response = execution.Result.Data!;

        Assert.Equal(requestedSeatIds.Count, response.UpdatedCount);
        Assert.Equal(requestedSeatIds.Order(), response.Seats.Select(item => item.SeatId).Order());
        Assert.All(response.Seats, item => Assert.Equal("DISABLED", item.SeatStatus));
        Assert.Equal(requestedSeatIds.Count, execution.UpdatedSeats.Count);
        Assert.Equal(requestedSeatIds.Order(), execution.UpdatedSeats.Select(item => item.SeatId));
        Assert.All(execution.UpdatedSeats, item => Assert.Equal("DISABLED", item.SeatStatus));
        Assert.Equal(1, execution.UpdateCommandCount);
    }

    private sealed class QueryCountDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private QueryCountDatabase(SqliteConnection connection, SqliteAuthDbContext db, SeatZoneCommandCounter counter)
        {
            _connection = connection;
            Db = db;
            Counter = counter;
        }

        public SqliteAuthDbContext Db { get; }
        public SeatZoneCommandCounter Counter { get; }

        public static async Task<QueryCountDatabase> CreateAsync(DbCommandInterceptor? additionalInterceptor = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var counter = new SeatZoneCommandCounter();
            var options = new DbContextOptionsBuilder<SqliteAuthDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(additionalInterceptor is null
                    ? [counter]
                    : [counter, additionalInterceptor])
                .Options;
            var db = new SqliteAuthDbContext(options);
            await db.Database.EnsureCreatedAsync();
            return new QueryCountDatabase(connection, db, counter);
        }

        public async Task SeedSessionMapAsync(int seatCount)
        {
            Db.AddRange(
                CreateCategory(),
                CreateShow(),
                new SeatMap
                {
                    SeatMapId = 30,
                    VenueId = 5,
                    MapCode = "MAIN",
                    MapName = "主厅",
                    MapVersion = "1.0",
                    MapStatus = "ENABLED"
                },
                new ShowSession
                {
                    SessionId = 10,
                    ShowId = 20,
                    SeatMapId = 30,
                    StartTime = Now.UtcDateTime.AddDays(1),
                    EndTime = Now.UtcDateTime.AddDays(1).AddHours(2),
                    SaleStartTime = Now.UtcDateTime.AddHours(-1),
                    SaleEndTime = Now.UtcDateTime.AddHours(1),
                    SessionStatus = "ONSALE"
                },
                CreateSection(40, 30, "A", true),
                CreateSection(41, 30, "EMPTY", true),
                new SeatLock
                {
                    SeatLockId = 60,
                    SessionId = 10,
                    SeatId = 1,
                    UserId = 7,
                    LockToken = "active-lock",
                    LockStatus = "ACTIVE",
                    LockTime = Now.UtcDateTime.AddMinutes(-1),
                    ExpireTime = Now.UtcDateTime.AddMinutes(9)
                },
                new SeatLock
                {
                    SeatLockId = 61,
                    SessionId = 10,
                    SeatId = 3,
                    UserId = 8,
                    LockToken = "expired-lock",
                    LockStatus = "ACTIVE",
                    LockTime = Now.UtcDateTime.AddMinutes(-20),
                    ExpireTime = Now.UtcDateTime.AddMinutes(-10)
                },
                new SeatReservation
                {
                    SeatReservationId = 70,
                    SessionId = 10,
                    SeatId = 2,
                    ReservationType = "SYSTEM",
                    ReservationStatus = "ACTIVE",
                    ReserveTime = Now.UtcDateTime.AddMinutes(-1)
                });
            Db.Seats.AddRange(Enumerable.Range(1, seatCount).Select(index => CreateSeat(index, 40, index != 4)));
            await Db.SaveChangesAsync();
        }

        public async Task SeedSellableSessionAsync(int seatCount)
        {
            Db.Add(new ShowSession
            {
                SessionId = 10,
                ShowId = 20,
                SeatMapId = 30,
                StartTime = Now.UtcDateTime.AddDays(1),
                EndTime = Now.UtcDateTime.AddDays(1).AddHours(2),
                SaleStartTime = Now.UtcDateTime.AddHours(-1),
                SaleEndTime = Now.UtcDateTime.AddHours(1),
                SessionStatus = "ONSALE"
            });
            Db.AddRange(CreateCategory(), CreateShow());
            await SeedSeatSectionAsync(seatCount);
        }

        public async Task SeedSeatSectionAsync(int seatCount)
        {
            Db.AddRange(
                new SeatMap
                {
                    SeatMapId = 30,
                    VenueId = 5,
                    MapCode = "MAIN",
                    MapName = "主厅",
                    MapVersion = "1.0",
                    MapStatus = "ENABLED"
                },
                CreateSection(40, 30, "A", true));
            Db.Seats.AddRange(Enumerable.Range(1, seatCount).Select(index => CreateSeat(index, 40, true)));
            await Db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }

        private static SeatSection CreateSection(long sectionId, long mapId, string code, bool isSellable) => new()
        {
            SeatSectionId = sectionId,
            SeatMapId = mapId,
            SectionCode = code,
            SectionName = $"{code}区",
            SectionType = "NORMAL",
            IsSellable = isSellable,
            DisplayOrder = checked((int)sectionId)
        };

        private static Category CreateCategory() => new()
        {
            CategoryId = 1,
            CategoryName = "测试分类",
            Status = 1
        };

        private static Show CreateShow() => new()
        {
            ShowId = 20,
            CategoryId = 1,
            ShowName = "测试演出",
            Status = "PUBLISHED",
            AuditStatus = "APPROVED"
        };

        private static Seat CreateSeat(int index, long sectionId, bool isSellable) => new()
        {
            SeatId = index,
            SeatSectionId = sectionId,
            RowCode = $"R{(index - 1) / 20 + 1}",
            SeatNo = ((index - 1) % 20 + 1).ToString(),
            RowIndex = (index - 1) / 20,
            ColIndex = (index - 1) % 20,
            SeatType = "NORMAL",
            SeatStatus = "ENABLED",
            IsSellable = isSellable
        };
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class RecordingSeatLockGuard : ISeatLockGuard
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

    private sealed class RecordingSeatLockBatchWriter(
        int? persistCount = null,
        bool throwAfterWrite = false) : ISeatLockBatchWriter
    {
        public int CallCount { get; private set; }
        public int ReceivedCount { get; private set; }
        public IReadOnlyList<EntityState> ReceivedStates { get; private set; } = [];

        public bool CanWrite(AppDbContext dbContext) => true;

        public async Task InsertAsync(
            AppDbContext dbContext,
            IReadOnlyList<SeatLock> locks,
            CancellationToken cancellationToken)
        {
            CallCount++;
            ReceivedCount = locks.Count;
            ReceivedStates = locks.Select(item => dbContext.Entry(item).State).ToArray();

            var count = Math.Clamp(persistCount ?? locks.Count, 0, locks.Count);
            dbContext.SeatLocks.AddRange(locks.Take(count).Select(Clone));
            await dbContext.SaveChangesAsync(cancellationToken);

            if (throwAfterWrite)
            {
                throw new InvalidOperationException("Injected native batch writer failure.");
            }
        }

        private static SeatLock Clone(SeatLock source) => new()
        {
            SessionId = source.SessionId,
            SeatId = source.SeatId,
            UserId = source.UserId,
            LockToken = source.LockToken,
            LockStatus = source.LockStatus,
            LockTime = source.LockTime,
            ExpireTime = source.ExpireTime,
            ReleaseTime = source.ReleaseTime,
            Remark = source.Remark,
            CreateBy = source.CreateBy,
            UpdateBy = source.UpdateBy
        };
    }

    private sealed class ThrowingUpdateInterceptor : DbCommandInterceptor
    {
        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result)
        {
            ThrowIfUpdate(command);
            return base.NonQueryExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            ThrowIfUpdate(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            ThrowIfUpdate(command);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ThrowIfUpdate(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private static void ThrowIfUpdate(DbCommand command)
        {
            if (command.CommandText.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Injected ExecuteUpdate failure.");
        }
    }
}
