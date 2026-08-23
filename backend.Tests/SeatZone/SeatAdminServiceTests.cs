using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.SeatZone;
using ShowtimeBackend.Entities.SeatZone;
using ShowtimeBackend.Services.SeatZone;

namespace ShowtimeBackend.Tests.SeatZone;

public sealed class SeatAdminServiceTests
{
    [Fact]
    public async Task UpdateSeatsAsync_UpdatesOnlyRequestedFields()
    {
        await using var db = CreateDbContext();
        await SeedSeatSectionAsync(db, 40);
        await SeedSeatAsync(db, 401, 40, "A", "1", 0, 0);
        await SeedSeatAsync(db, 402, 40, "A", "2", 0, 1);
        var service = new SeatAdminService(db);

        var result = await service.UpdateSeatsAsync(
            40,
            new SeatBatchUpdateRequest([401, 402], null, "DISABLED", null, false),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Data!.UpdatedCount);
        Assert.All(result.Data.Seats, seat =>
        {
            Assert.Equal("DISABLED", seat.SeatStatus);
            Assert.False(seat.IsSellable);
            Assert.Equal("NORMAL", seat.SeatType);
            Assert.False(seat.IsAisleSide);
        });
    }

    [Fact]
    public async Task UpdateSeatsAsync_UpdatesAllEditableFields()
    {
        await using var db = CreateDbContext();
        await SeedSeatSectionAsync(db, 40);
        await SeedSeatAsync(db, 401, 40, "A", "1", 0, 0);
        var service = new SeatAdminService(db);

        var result = await service.UpdateSeatsAsync(
            40,
            new SeatBatchUpdateRequest([401], "ACCESSIBLE", "MAINTENANCE", true, false),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var seat = Assert.Single(result.Data!.Seats);
        Assert.Equal("ACCESSIBLE", seat.SeatType);
        Assert.Equal("MAINTENANCE", seat.SeatStatus);
        Assert.True(seat.IsAisleSide);
        Assert.False(seat.IsSellable);
    }

    [Fact]
    public async Task UpdateSeatsAsync_RejectsEmptyIdsAndDoesNotChangeData()
    {
        await using var db = CreateDbContext();
        await SeedSeatSectionAsync(db, 40);
        await SeedSeatAsync(db, 401, 40, "A", "1", 0, 0);
        var service = new SeatAdminService(db);

        var result = await service.UpdateSeatsAsync(
            40,
            new SeatBatchUpdateRequest([], "ACCESSIBLE", null, null, null),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal("SEAT_BATCH_UPDATE_INVALID_REQUEST", result.Title);
        Assert.Equal("NORMAL", (await db.Seats.SingleAsync()).SeatType);
    }

    [Fact]
    public async Task UpdateSeatsAsync_RejectsRequestWithoutPatchFields()
    {
        await using var db = CreateDbContext();
        await SeedSeatSectionAsync(db, 40);
        await SeedSeatAsync(db, 401, 40, "A", "1", 0, 0);
        var service = new SeatAdminService(db);

        var result = await service.UpdateSeatsAsync(
            40,
            new SeatBatchUpdateRequest([401], null, null, null, null),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal("SEAT_BATCH_UPDATE_INVALID_REQUEST", result.Title);
        var seat = await db.Seats.SingleAsync();
        Assert.Equal("NORMAL", seat.SeatType);
        Assert.Equal("ENABLED", seat.SeatStatus);
        Assert.True(seat.IsSellable);
    }

    [Fact]
    public async Task UpdateSeatsAsync_RejectsDuplicateIds()
    {
        await using var db = CreateDbContext();
        await SeedSeatSectionAsync(db, 40);
        var service = new SeatAdminService(db);

        var result = await service.UpdateSeatsAsync(
            40,
            new SeatBatchUpdateRequest([401, 401], null, "DISABLED", null, null),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal("SEAT_BATCH_UPDATE_INVALID_REQUEST", result.Title);
    }

    [Fact]
    public async Task UpdateSeatsAsync_RejectsNonPositiveIds()
    {
        await using var db = CreateDbContext();
        var service = new SeatAdminService(db);

        var result = await service.UpdateSeatsAsync(
            40,
            new SeatBatchUpdateRequest([0], null, "DISABLED", null, null),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal("SEAT_BATCH_UPDATE_INVALID_REQUEST", result.Title);
    }

    [Fact]
    public async Task UpdateSeatsAsync_RejectsMoreThan999Ids()
    {
        await using var db = CreateDbContext();
        var service = new SeatAdminService(db);
        var seatIds = Enumerable.Range(1, 1000).Select(id => (long)id).ToArray();

        var result = await service.UpdateSeatsAsync(
            40,
            new SeatBatchUpdateRequest(seatIds, null, "DISABLED", null, null),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal("SEAT_BATCH_UPDATE_INVALID_REQUEST", result.Title);
    }

    [Fact]
    public async Task UpdateSeatsAsync_RejectsSeatOutsideSectionWithoutPartialUpdate()
    {
        await using var db = CreateDbContext();
        await SeedSeatSectionAsync(db, 40);
        await SeedSeatSectionAsync(db, 41);
        await SeedSeatAsync(db, 401, 40, "A", "1", 0, 0);
        await SeedSeatAsync(db, 411, 41, "A", "1", 0, 0);
        var service = new SeatAdminService(db);

        var result = await service.UpdateSeatsAsync(
            40,
            new SeatBatchUpdateRequest([401, 411], null, "DISABLED", null, false),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        Assert.Equal("SEAT_BATCH_UPDATE_SEAT_NOT_FOUND", result.Title);
        Assert.All(await db.Seats.OrderBy(seat => seat.SeatId).ToListAsync(), seat =>
            Assert.Equal("ENABLED", seat.SeatStatus));
    }

    [Theory]
    [InlineData("UNKNOWN", null)]
    [InlineData(null, "UNKNOWN")]
    public async Task UpdateSeatsAsync_RejectsInvalidEnumValues(
        string? seatType,
        string? seatStatus)
    {
        await using var db = CreateDbContext();
        await SeedSeatSectionAsync(db, 40);
        var service = new SeatAdminService(db);

        var result = await service.UpdateSeatsAsync(
            40,
            new SeatBatchUpdateRequest([401], seatType, seatStatus, null, null),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal("SEAT_BATCH_UPDATE_INVALID_REQUEST", result.Title);
    }

    private static AppDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task SeedSeatSectionAsync(AppDbContext db, long seatSectionId)
    {
        var seatMapId = 1000 + seatSectionId;
        var seatMap = new SeatMap
        {
            SeatMapId = seatMapId,
            VenueId = 1,
            MapCode = $"MAP-{seatSectionId}",
            MapName = "测试座位图",
            MapVersion = "V1",
            IsDefault = true,
            MapStatus = "ENABLED"
        };
        db.SeatMaps.Add(seatMap);
        db.SeatSections.Add(new SeatSection
        {
            SeatSectionId = seatSectionId,
            SeatMapId = seatMapId,
            SectionCode = $"SECTION-{seatSectionId}",
            SectionName = "测试票区",
            SectionType = "NORMAL",
            IsSellable = true,
            DisplayOrder = 1,
            SeatMap = seatMap
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedSeatAsync(
        AppDbContext db,
        long seatId,
        long seatSectionId,
        string rowCode,
        string seatNo,
        int rowIndex,
        int colIndex)
    {
        db.Seats.Add(new Seat
        {
            SeatId = seatId,
            SeatSectionId = seatSectionId,
            RowCode = rowCode,
            SeatNo = seatNo,
            RowIndex = rowIndex,
            ColIndex = colIndex,
            XCoord = 100m,
            YCoord = 50m,
            SeatType = "NORMAL",
            SeatStatus = "ENABLED",
            IsSellable = true
        });
        await db.SaveChangesAsync();
    }
}
