using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Common;
using ShowtimeBackend.Controllers.SeatZone;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.SeatZone;
using ShowtimeBackend.Entities.SeatZone;

namespace ShowtimeBackend.Tests.SeatZone;

public sealed class SeatsControllerTests
{
    [Fact]
    public async Task UpdateBatch_ReturnsUpdatedSeats()
    {
        await using var db = CreateDbContext();
        await SeedSeatAsync(db, 401, 40, "A", "1", 0, 0);
        await SeedSeatAsync(db, 402, 40, "A", "2", 0, 1);
        var controller = new SeatsController(db);

        var action = await controller.UpdateBatch(
            40,
            new SeatBatchUpdateRequest([401, 402], null, "DISABLED", null, false),
            CancellationToken.None);

        var result = Assert.IsType<OkObjectResult>(action.Result);
        var response = Assert.IsType<ApiResponse<SeatBatchUpdateResponse>>(result.Value);
        Assert.True(response.Success);
        Assert.Equal(2, response.Data!.UpdatedCount);
    }

    [Fact]
    public async Task UpdateBatch_ReturnsStableCodeForMissingSeat()
    {
        await using var db = CreateDbContext();
        await SeedSeatAsync(db, 401, 40, "A", "1", 0, 0);
        var controller = new SeatsController(db);

        var action = await controller.UpdateBatch(
            40,
            new SeatBatchUpdateRequest([401, 999], null, "DISABLED", null, null),
            CancellationToken.None);

        var result = Assert.IsType<ObjectResult>(action.Result);
        Assert.Equal(StatusCodes.Status404NotFound, result.StatusCode);
        var response = Assert.IsType<ApiResponse<SeatBatchUpdateResponse>>(result.Value);
        Assert.Equal("Seat not found", response.Code);
    }

    [Fact]
    public void ControllerRequiresAdminRole()
    {
        var authorize = Assert.Single(
            typeof(SeatsController).GetCustomAttributes(typeof(AuthorizeAttribute), true));
        var attribute = Assert.IsType<AuthorizeAttribute>(authorize);
        Assert.Equal("Admin", attribute.Roles);
    }

    private static AppDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task SeedSeatAsync(
        AppDbContext db,
        long seatId,
        long seatSectionId,
        string rowCode,
        string seatNo,
        int rowIndex,
        int colIndex)
    {
        if (!await db.SeatSections.AnyAsync(section => section.SeatSectionId == seatSectionId))
        {
            var seatMap = new SeatMap
            {
                SeatMapId = 1040,
                VenueId = 1,
                MapCode = "MAP-40",
                MapName = "测试座位图",
                MapVersion = "V1",
                IsDefault = true,
                MapStatus = "ENABLED"
            };
            db.SeatMaps.Add(seatMap);
            db.SeatSections.Add(new SeatSection
            {
                SeatSectionId = seatSectionId,
                SeatMapId = seatMap.SeatMapId,
                SectionCode = "SECTION-40",
                SectionName = "测试票区",
                SectionType = "NORMAL",
                IsSellable = true,
                DisplayOrder = 1,
                SeatMap = seatMap
            });
        }

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
