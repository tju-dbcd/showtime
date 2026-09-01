using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
        Assert.Single(small.Result.Value!.Locks);
        Assert.Equal(100, large.Result.Value!.Locks.Count);
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
        Assert.Equal(1, small.Result.Data!.UpdatedCount);
        Assert.Equal(100, large.Result.Data!.UpdatedCount);
        Assert.Equal(small.ReadCommandCount, large.ReadCommandCount);
        Assert.Equal(2, small.ReadCommandCount);
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

    private static async Task<(SeatZoneResult<SeatLockBatchResponse> Result, int ReadCommandCount)> LockSeatsAsync(int seatCount)
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

        return (result, database.Counter.ReadCommandCount);
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

    private static async Task<(ServiceResult<SeatBatchUpdateResponse> Result, int ReadCommandCount)> UpdateSeatsAsync(int seatCount)
    {
        await using var database = await QueryCountDatabase.CreateAsync();
        await database.SeedSeatSectionAsync(seatCount);
        database.Counter.Reset();

        var result = await new SeatAdminService(database.Db).UpdateSeatsAsync(
            40,
            new SeatBatchUpdateRequest(SeatIds(seatCount), null, "DISABLED", null, null),
            CancellationToken.None);

        return (result, database.Counter.ReadCommandCount);
    }

    private static IReadOnlyList<long> SeatIds(int count) =>
        Enumerable.Range(1, count).Select(index => (long)index).ToArray();

    private static int GetSeatCount(ServiceResult<SessionSeatMapDto> result) =>
        result.Data!.SeatMap.Sections.Sum(section => section.Seats.Count);

    private static string GetAvailability(ServiceResult<SessionSeatMapDto> result, long seatId) =>
        result.Data!.SeatMap.Sections.SelectMany(section => section.Seats)
            .Single(seat => seat.SeatId == seatId).AvailabilityStatus;

    private sealed class QueryCountDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private QueryCountDatabase(SqliteConnection connection, AppDbContext db, SeatZoneCommandCounter counter)
        {
            _connection = connection;
            Db = db;
            Counter = counter;
        }

        public AppDbContext Db { get; }
        public SeatZoneCommandCounter Counter { get; }

        public static async Task<QueryCountDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var counter = new SeatZoneCommandCounter();
            var options = new DbContextOptionsBuilder<SqliteAuthDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(counter)
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
}
