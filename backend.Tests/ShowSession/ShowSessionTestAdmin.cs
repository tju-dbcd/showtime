using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ShowtimeBackend.Common;
using ShowtimeBackend.Controllers.ShowSession.Admin;
using ShowtimeBackend.Controllers.ShowSession.Client;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.ShowSessionChange;
using ShowtimeBackend.DTOs.ShowSessionDto;
using ShowtimeBackend.Entities.ShowSession;
using ShowtimeBackend.Services.Impl;

namespace ShowtimeBackend.Tests.ShowSessionTest;

public sealed class ShowSessionAdminControllersTests
{
    [Fact]
    public async Task GetAdminSessions_WhenSessionsExist_ReturnsSessionList()
    {
        await using var db = CreateDbContext();
        long targetShowId = 1;

        db.ShowSessions.AddRange(
            CreateSessionEntity(targetShowId, "ONSALE", DateTime.UtcNow.AddDays(1)),
            CreateSessionEntity(targetShowId, "UPCOMING", DateTime.UtcNow.AddDays(2)),
            CreateSessionEntity(showId: 99, "ONSALE", DateTime.UtcNow.AddDays(1))
        );
        await db.SaveChangesAsync();

        var controller = CreateAdminController(db);

        var actionResult = await controller.GetAdminSessions(targetShowId, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<IEnumerable<ShowSessionDto>>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.NotNull(apiResponse.Data);

        var sessions = apiResponse.Data.ToList();
        Assert.Equal(2, sessions.Count);
        Assert.All(sessions, s => Assert.Equal(targetShowId, s.ShowId));
    }

    [Fact]
    public async Task CreateSession_WithValidRequest_ReturnsCreatedAndPersistsToDb()
    {
        await using var db = CreateDbContext();
        var controller = CreateAdminController(db);
        var request = CreateValidSessionRequest(
            startTime: DateTime.UtcNow.AddDays(10),
            endTime: DateTime.UtcNow.AddDays(10).AddHours(2));

        var actionResult = await controller.CreateSession(1, request, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<ShowSessionDto>>(createdResult.Value);
        Assert.True(apiResponse.Success);
        Assert.NotNull(apiResponse.Data);
        Assert.Equal(1, apiResponse.Data.ShowId);
        Assert.Equal(SessionStatus.PRESALE, apiResponse.Data.SessionStatus);

        var dbSession = await db.ShowSessions.FirstOrDefaultAsync(s => s.SessionId == apiResponse.Data.SessionId);
        Assert.NotNull(dbSession);
        Assert.Equal(request.SeatMapId, dbSession.SeatMapId);
    }

    [Fact]
    public async Task CreateSession_WhenEndTimeBeforeStartTime_ReturnsBadRequest()
    {
        await using var db = CreateDbContext();
        var controller = CreateAdminController(db);
        var invalidRequest = CreateValidSessionRequest(
            startTime: DateTime.UtcNow.AddDays(10),
            endTime: DateTime.UtcNow.AddDays(10).AddHours(-1));

        var actionResult = await controller.CreateSession(1, invalidRequest, CancellationToken.None);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<ShowSessionDto>>(badRequestResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Equal("INVALID_ARGUMENT", apiResponse.Code);
    }

    [Fact]
    public async Task CreateSession_WhenScheduleConflicts_ReturnsConflict()
    {
        await using var db = CreateDbContext();
        var baseTime = DateTime.UtcNow.AddDays(5);

        db.ShowSessions.Add(new ShowSession
        {
            ShowId = 1,
            SeatMapId = 100,
            StartTime = baseTime,
            EndTime = baseTime.AddHours(2),
            SaleStartTime = baseTime.AddDays(-1),
            SaleEndTime = baseTime,
            SessionStatus = "ONSALE"
        });
        await db.SaveChangesAsync();

        var controller = CreateAdminController(db);
        var conflictRequest = CreateValidSessionRequest(
            startTime: baseTime.AddHours(1),
            endTime: baseTime.AddHours(3),
            seatMapId: 100);

        var actionResult = await controller.CreateSession(1, conflictRequest, CancellationToken.None);

        var conflictResult = Assert.IsType<ConflictObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<ShowSessionDto>>(conflictResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Equal("OPERATION_CONFLICT", apiResponse.Code);
    }

    [Fact]
    public async Task ConfigurePriceStrategies_WhenSessionNotExists_ReturnsNotFound()
    {
        await using var db = CreateDbContext();
        var controller = CreateAdminController(db);

        // 使用具名参数，显式传参
        var requests = new[]
        {
            new CreatePriceStrategyRequest(
                SeatSectionId: 1,
                StrategyName: "VIP策略",
                PriceType: PriceType.VIP,
                Price: 580m,
                SaleStartTime: null,
                SaleEndTime: null)
        };

        var actionResult = await controller.ConfigurePriceStrategies(999, requests, CancellationToken.None);

        var notFoundResult = Assert.IsType<NotFoundObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<object>>(notFoundResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Equal("NOT_FOUND", apiResponse.Code);
    }

    [Fact]
    public async Task ConfigurePriceStrategies_WithEmptyRequests_ReturnsBadRequest()
    {
        await using var db = CreateDbContext();
        var session = SeedShowSession(db, 1, 10);
        var controller = CreateAdminController(db);
        var requests = Array.Empty<CreatePriceStrategyRequest>();

        var actionResult = await controller.ConfigurePriceStrategies(session.SessionId, requests, CancellationToken.None);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<object>>(badRequestResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Equal("INVALID_ARGUMENT", apiResponse.Code);
    }

    [Fact]
    public async Task ConfigurePriceStrategies_WhenValid_ClearsOldAndInsertsNewStrategies()
    {
        await using var db = CreateDbContext();
        var session = SeedShowSession(db, 1, 10);

        db.PriceStrategy.Add(new PriceStrategy
        {
            SessionId = session.SessionId,
            SeatSectionId = 1,
            StrategyName = "旧策略",
            PriceType = "STANDARD",
            Price = 100m,
            Status = "ENABLED"
        });
        await db.SaveChangesAsync();

        var controller = CreateAdminController(db);

        // 使用具名参数，显式传参 OriginalPrice
        var newRequests = new[]
        {
            new CreatePriceStrategyRequest(
                SeatSectionId: 1,
                StrategyName: "VIP票策略",
                PriceType: PriceType.VIP,
                Price: 880m,
                SaleStartTime: null,
                SaleEndTime: null),
            new CreatePriceStrategyRequest(
                SeatSectionId: 2,
                StrategyName: "早鸟票策略",
                PriceType: PriceType.EARLY_BIRD,
                Price: 280m,
                SaleStartTime: null,
                SaleEndTime: null)
        };

        var actionResult = await controller.ConfigurePriceStrategies(session.SessionId, newRequests, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<object>>(okResult.Value);
        Assert.True(apiResponse.Success);

        var strategiesInDb = await db.PriceStrategy.Where(p => p.SessionId == session.SessionId).ToListAsync();
        Assert.Equal(2, strategiesInDb.Count);
        Assert.Contains(strategiesInDb, p => p.PriceType == "VIP" && p.Price == 880m);
        Assert.Contains(strategiesInDb, p => p.PriceType == "EARLY_BIRD" && p.Price == 280m);
    }

    [Fact]
    public async Task UpdateSessionStatus_WhenValidStatus_UpdatesDatabase()
    {
        await using var db = CreateDbContext();
        var session = SeedShowSession(db, 1, 10, initialStatus: "UPCOMING");
        var controller = CreateAdminController(db);

        var actionResult = await controller.UpdateSessionStatus(
            session.SessionId,
            new UpdateSessionStatusRequest(SessionStatus.ONSALE),
            CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<object>>(okResult.Value);
        Assert.True(apiResponse.Success);

        var updatedSession = await db.ShowSessions.FindAsync(session.SessionId);
        Assert.Equal("ONSALE", updatedSession!.SessionStatus);
    }

    [Fact]
    public async Task UpdateSessionStatus_WhenSessionNotExists_ReturnsNotFound()
    {
        await using var db = CreateDbContext();
        var controller = CreateAdminController(db);

        var actionResult = await controller.UpdateSessionStatus(
            9999,
            new UpdateSessionStatusRequest(SessionStatus.ONSALE),
            CancellationToken.None);

        var notFoundResult = Assert.IsType<NotFoundObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<object>>(notFoundResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Equal("NOT_FOUND", apiResponse.Code);
    }

    // ==========================================
    // Helper Methods
    // ==========================================

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new AppDbContext(options);
    }

    private static AdminShowSessionController CreateAdminController(AppDbContext db, ClaimsPrincipal? user = null)
    {
        var adminService = new AdminShowSessionService(db);
        return new AdminShowSessionController(adminService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = user ?? CreateAdminClaimsPrincipal()
                }
            }
        };
    }

    private static ClaimsPrincipal CreateAdminClaimsPrincipal()
    {
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim("sub", "1001"),
                new Claim(ClaimTypes.Name, "AdminUser"),
                new Claim(ClaimTypes.Role, "Admin")
            },
            "TestAuthType");

        return new ClaimsPrincipal(identity);
    }

    private static CreateShowSessionRequest CreateValidSessionRequest(
        DateTime startTime,
        DateTime endTime,
        long seatMapId = 10)
    {
        return new CreateShowSessionRequest(
            StartTime: startTime,
            EndTime: endTime,
            SaleStartTime: startTime.AddDays(-5),
            SaleEndTime: startTime.AddHours(-1),
            SeatMapId: seatMapId
        );
    }

    private static ShowSession SeedShowSession(
        AppDbContext db,
        long showId,
        long seatMapId,
        string initialStatus = "UPCOMING")
    {
        var session = new ShowSession
        {
            ShowId = showId,
            SeatMapId = seatMapId,
            StartTime = DateTime.UtcNow.AddDays(10),
            EndTime = DateTime.UtcNow.AddDays(10).AddHours(2),
            SaleStartTime = DateTime.UtcNow.AddDays(1),
            SaleEndTime = DateTime.UtcNow.AddDays(9),
            SessionStatus = initialStatus,
            CreateTime = DateTime.UtcNow
        };

        db.ShowSessions.Add(session);
        db.SaveChanges();
        return session;
    }

    private static ShowSession CreateSessionEntity(long showId, string status, DateTime startTime)
    {
        return new ShowSession
        {
            ShowId = showId,
            SeatMapId = 1,
            StartTime = startTime,
            EndTime = startTime.AddHours(2),
            SaleStartTime = startTime.AddDays(-2),
            SaleEndTime = startTime.AddHours(-1),
            SessionStatus = status,
            CreateTime = DateTime.UtcNow
        };
    }
}
