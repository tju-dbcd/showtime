using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Data;
using ShowtimeBackend.Entities.SeatZone;
using ShowtimeBackend.Entities.ShowSession;
using ShowtimeBackend.Services.SeatZone;

namespace ShowtimeBackend.Tests.SeatZone;

public sealed class SessionSeatMapQueryServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetAsync_ReturnsLockAndReservationAvailability()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AppDbContext(options);
        db.AddRange(
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
            new SeatSection
            {
                SeatSectionId = 40,
                SeatMapId = 30,
                SectionCode = "A",
                SectionName = "A区",
                IsSellable = true
            },
            CreateSeat(50, true),
            CreateSeat(51, true),
            CreateSeat(52, true),
            CreateSeat(53, false),
            new SeatLock
            {
                SeatLockId = 60,
                SessionId = 10,
                SeatId = 50,
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
                SeatId = 52,
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
                SeatId = 51,
                OrderItemId = 80,
                ReservationType = "ORDER",
                ReservationStatus = "ACTIVE",
                ReserveTime = Now.UtcDateTime.AddMinutes(-1)
            });
        await db.SaveChangesAsync();
        var service = new SessionSeatMapQueryService(
            db,
            new FixedTimeProvider(Now));

        var result = await service.GetAsync(10, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var statuses = result.Data!.SeatMap.Sections
            .SelectMany(section => section.Seats)
            .ToDictionary(seat => seat.SeatId, seat => seat.AvailabilityStatus);
        Assert.Equal("LOCKED", statuses[50]);
        Assert.Equal("RESERVED", statuses[51]);
        Assert.Equal("AVAILABLE", statuses[52]);
        Assert.Equal("UNAVAILABLE", statuses[53]);
    }

    private static Seat CreateSeat(long seatId, bool isSellable) => new()
    {
        SeatId = seatId,
        SeatSectionId = 40,
        RowCode = "1",
        SeatNo = seatId.ToString(),
        RowIndex = 1,
        ColIndex = (int)(seatId - 49),
        IsSellable = isSellable,
        SeatStatus = "ENABLED"
    };

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
