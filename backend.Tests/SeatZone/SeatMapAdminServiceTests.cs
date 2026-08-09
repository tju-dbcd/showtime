using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.SeatZone;
using ShowtimeBackend.Entities.SeatZone;
using ShowtimeBackend.Entities.ShowSession;
using ShowtimeBackend.Services.SeatZone;

namespace ShowtimeBackend.Tests.SeatZone;

public sealed class SeatMapAdminServiceTests
{
    [Fact]
    public async Task ListMapsAsync_ReturnsVenueName()
    {
        await using var db = CreateDbContext();
        var seatMap = await SeedSeatMapAsync(db, 11, "天津大礼堂", 101, "一层座位图");
        var service = new SeatMapAdminService(db);

        var result = await service.ListMapsAsync(
            new SeatMapListQuery(null, "ENABLED", null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Data!.Items);
        Assert.Equal(seatMap.SeatMapId, item.SeatMapId);
        AssertVenueName(item, "天津大礼堂");
    }

    [Fact]
    public async Task GetMapAsync_ReturnsVenueName()
    {
        await using var db = CreateDbContext();
        var seatMap = await SeedSeatMapAsync(db, 12, "天津音乐厅", 102, "标准座位图");
        var service = new SeatMapAdminService(db);

        var result = await service.GetMapAsync(seatMap.SeatMapId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        AssertVenueName(result.Data!, "天津音乐厅");
    }

    [Fact]
    public async Task CreateMapAsync_ReturnsVenueName()
    {
        await using var db = CreateDbContext();
        db.Venues.Add(new Venue
        {
            VenueId = 21,
            VenueName = "天津体育馆",
            Status = "ENABLED"
        });
        await db.SaveChangesAsync();
        var service = new SeatMapAdminService(db);

        var result = await service.CreateMapAsync(
            CreateRequest(21, "NEW-MAP", "比赛座位图"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("天津体育馆", result.Data!.VenueName);
    }

    [Fact]
    public async Task UpdateMapAsync_WhenVenueChanges_ReturnsNewVenueName()
    {
        await using var db = CreateDbContext();
        var seatMap = await SeedSeatMapAsync(db, 31, "原场馆", 301, "原座位图");
        db.Venues.Add(new Venue
        {
            VenueId = 32,
            VenueName = "新场馆",
            Status = "ENABLED"
        });
        await db.SaveChangesAsync();
        var service = new SeatMapAdminService(db);

        var result = await service.UpdateMapAsync(
            seatMap.SeatMapId,
            CreateRequest(32, "UPDATED-MAP", "更新后的座位图"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(32, result.Data!.VenueId);
        Assert.Equal("新场馆", result.Data.VenueName);
    }

    [Fact]
    public async Task CreateMapAsync_WhenVenueDoesNotExist_ReturnsNotFound()
    {
        await using var db = CreateDbContext();
        var service = new SeatMapAdminService(db);

        var result = await service.CreateMapAsync(
            CreateRequest(999, "MISSING-VENUE", "无场馆座位图"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static void AssertVenueName(SeatMapResponse response, string expected)
    {
        var json = JsonSerializer.Serialize(
            response,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);

        Assert.True(document.RootElement.TryGetProperty("venueName", out var venueName));
        Assert.Equal(expected, venueName.GetString());
    }

    private static SeatMapRequest CreateRequest(
        long venueId,
        string mapCode,
        string mapName) => new(
            venueId,
            mapCode,
            mapName,
            "V1",
            true,
            1200m,
            800m,
            "ENABLED",
            null);

    private static async Task<SeatMap> SeedSeatMapAsync(
        AppDbContext db,
        long venueId,
        string venueName,
        long seatMapId,
        string mapName)
    {
        db.Venues.Add(new Venue
        {
            VenueId = venueId,
            VenueName = venueName,
            Status = "ENABLED"
        });
        var seatMap = new SeatMap
        {
            SeatMapId = seatMapId,
            VenueId = venueId,
            MapCode = $"MAP-{seatMapId}",
            MapName = mapName,
            MapVersion = "V1",
            IsDefault = true,
            MapStatus = "ENABLED"
        };
        db.SeatMaps.Add(seatMap);
        await db.SaveChangesAsync();
        return seatMap;
    }
}
