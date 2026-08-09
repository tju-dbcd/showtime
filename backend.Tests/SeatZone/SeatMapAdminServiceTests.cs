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
