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

public sealed class ShowSessionClientControllersTests
{
    [Fact]
    public async Task GetOnSaleSessions_WhenShowIdInvalid_ReturnsBadRequest()
    {
        await using var db = CreateDbContext();
        var controller = CreateClientController(db);

        var actionResult = await controller.GetOnSaleSessions(0, CancellationToken.None);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<IEnumerable<ShowSessionDto>>>(badRequestResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Equal("INVALID_PARAM", apiResponse.Code);
    }

    [Fact]
    public async Task GetOnSaleSessions_WhenValid_ReturnsOnlyOnSaleSessions()
    {
        await using var db = CreateDbContext();
        long targetShowId = 1;

        // 植入测试数据：包含 ONSALE、UPCOMING、ENDED 三种状态
        db.ShowSessions.AddRange(
            CreateSessionEntity(targetShowId, "ONSALE", DateTime.UtcNow.AddDays(1)),
            CreateSessionEntity(targetShowId, "UPCOMING", DateTime.UtcNow.AddDays(2)),
            CreateSessionEntity(targetShowId, "ENDED", DateTime.UtcNow.AddDays(-1)),
            CreateSessionEntity(showId: 2, "ONSALE", DateTime.UtcNow.AddDays(1)) // 其他演出的场次
        );
        await db.SaveChangesAsync();

        var controller = CreateClientController(db);

        var actionResult = await controller.GetOnSaleSessions(targetShowId, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<IEnumerable<ShowSessionDto>>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.NotNull(apiResponse.Data);

        var sessions = apiResponse.Data.ToList();
        Assert.Single(sessions); // 只能查到 targetShowId 且状态为 ONSALE 的 1 条记录
        Assert.Equal("ONSALE", sessions[0].SessionStatus);
        Assert.Equal(targetShowId, sessions[0].ShowId);
    }

    [Fact]
    public async Task GetPricingStrategies_WhenSessionIdInvalid_ReturnsBadRequest()
    {
        await using var db = CreateDbContext();
        var controller = CreateClientController(db);

        var actionResult = await controller.GetPricingStrategies(-5, CancellationToken.None);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<IEnumerable<PricingStrategyDto>>>(badRequestResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Equal("INVALID_PARAM", apiResponse.Code);
    }

    [Fact]
    public async Task GetPricingStrategies_WhenValid_ReturnsOnlyEnabledStrategies()
    {
        await using var db = CreateDbContext();
        long targetSessionId = 100;

        // 植入策略数据：包含 ENABLED 和 DISABLED 状态
        db.PriceStrategy.AddRange(
            new PriceStrategy { SessionId = targetSessionId, SeatSectionId = 1, PriceType = "VIP", Price = 880m, Status = "ENABLED" },
            new PriceStrategy { SessionId = targetSessionId, SeatSectionId = 2, PriceType = "STANDARD", Price = 380m, Status = "ENABLED" },
            new PriceStrategy { SessionId = targetSessionId, SeatSectionId = 3, PriceType = "EARLY_BIRD", Price = 180m, Status = "DISABLED" },
            new PriceStrategy { SessionId = 999, SeatSectionId = 1, PriceType = "VIP", Price = 880m, Status = "ENABLED" }
        );
        await db.SaveChangesAsync();

        var controller = CreateClientController(db);

        var actionResult = await controller.GetPricingStrategies(targetSessionId, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<IEnumerable<PricingStrategyDto>>>(okResult.Value);
        Assert.True(apiResponse.Success);

        var strategies = apiResponse.Data!.ToList();
        Assert.Equal(2, strategies.Count);
        Assert.All(strategies, s => Assert.Equal("ENABLED", s.Status));
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

    private static ShowSessionClientController CreateClientController(AppDbContext db)
    {
        var sessionService = new ShowSessionService(db);
        var showService = new ClientShowService(db);

        // 传入两个依赖项：ShowSessionService 和 ClientShowService，解决 CS7036 报错
        var controller = new ShowSessionClientController(sessionService, showService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        return controller;
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
