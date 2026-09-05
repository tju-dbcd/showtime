using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
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
    public async Task ConfigurePriceStrategies_WhenRequestsIsNull_ReturnsBadRequest()
    {
        await using var db = CreateDbContext();
        var session = SeedShowSession(db, 1, 10);
        var controller = CreateAdminController(db);

        var actionResult = await controller.ConfigurePriceStrategies(session.SessionId, null!, CancellationToken.None);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<object>>(badRequestResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Equal("INVALID_ARGUMENT", apiResponse.Code);
    }

    [Fact]
    public async Task ConfigurePriceStrategies_WithEmptyRequests_ClearsStrategiesAndReturnsOk()
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
        var requests = Array.Empty<CreatePriceStrategyRequest>();

        var actionResult = await controller.ConfigurePriceStrategies(session.SessionId, requests, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<object>>(okResult.Value);
        Assert.True(apiResponse.Success);

        var strategiesInDb = await db.PriceStrategy.Where(p => p.SessionId == session.SessionId).ToListAsync();
        Assert.Empty(strategiesInDb);
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

    [Fact]
    public async Task ConfigureDynamicPricingRules_WhenRequestsIsNull_ReturnsBadRequest()
    {
        await using var db = CreateDbContext();
        var session = SeedShowSession(db, 1, 10);
        var controller = CreateAdminController(db);

        var actionResult = await controller.ConfigureDynamicPricingRules(session.SessionId, null!, CancellationToken.None);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<object>>(badRequestResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Equal("INVALID_ARGUMENT", apiResponse.Code);
    }

    [Fact]
    public async Task ConfigureDynamicPricingRules_WithEmptyRequests_ClearsRulesAndReturnsOk()
    {
        await using var db = CreateDbContext();
        var session = SeedShowSession(db, 1, 10);

        db.DynamicPricingRules.Add(new DynamicPricingRule
        {
            SessionId = session.SessionId,
            RuleName = "旧规则",
            TriggerType = "TIME_WINDOW",
            AdjustmentType = "DISCOUNT_RATE",
            AdjustmentValue = 0.9m,
            Status = "ENABLED"
        });
        await db.SaveChangesAsync();

        var controller = CreateAdminController(db);
        var requests = Array.Empty<CreateDynamicPricingRuleRequest>();

        var actionResult = await controller.ConfigureDynamicPricingRules(session.SessionId, requests, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<object>>(okResult.Value);
        Assert.True(apiResponse.Success);

        var rulesInDb = await db.DynamicPricingRules.Where(r => r.SessionId == session.SessionId).ToListAsync();
        Assert.Empty(rulesInDb);
    }

    [Fact]
    public async Task ConfigureDynamicPricingRules_WithInvalidTriggerType_ReturnsBadRequest()
    {
        await using var db = CreateDbContext();
        var session = SeedShowSession(db, 1, 10);
        var controller = CreateAdminController(db);

        var invalidRequests = new[]
        {
            new CreateDynamicPricingRuleRequest(
                SeatSectionId: 1,
                RuleName: "促销规则",
                TriggerType: "INVALID_TRIGGERSSS",
                StartOffsetMinutes: 100,
                EndOffsetMinutes: 10,
                AdjustmentType: "DISCOUNT_RATE",
                AdjustmentValue: 0.8m,
                Priority: 1)
        };

        controller.ModelState.AddModelError("TriggerType", "TriggerType 必须为 TIME_WINDOW 或 INVENTORY_RATE");

        var actionResult = await controller.ConfigureDynamicPricingRules(session.SessionId, invalidRequests, CancellationToken.None);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<object>>(badRequestResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Equal("INVALID_ARGUMENT", apiResponse.Code);
    }

    [Fact]
    public async Task ConfigureDynamicPricingRules_WhenValid_ClearsOldAndInsertsNewRules()
    {
        await using var db = CreateDbContext();
        var session = SeedShowSession(db, 1, 10);

        db.DynamicPricingRules.Add(new DynamicPricingRule
        {
            SessionId = session.SessionId,
            RuleName = "旧规则",
            TriggerType = "TIME_WINDOW",
            AdjustmentType = "DISCOUNT_RATE",
            AdjustmentValue = 0.9m,
            Status = "ENABLED"
        });
        await db.SaveChangesAsync();

        var controller = CreateAdminController(db);
        var newRequests = new[]
        {
            new CreateDynamicPricingRuleRequest(
                SeatSectionId: 1,
                RuleName: "早鸟调价规则",
                TriggerType: "TIME_WINDOW",
                StartOffsetMinutes: 120,
                EndOffsetMinutes: 30,
                AdjustmentType: "DISCOUNT_RATE",
                AdjustmentValue: 0.75m,
                Priority: 10)
        };

        var actionResult = await controller.ConfigureDynamicPricingRules(session.SessionId, newRequests, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<object>>(okResult.Value);
        Assert.True(apiResponse.Success);

        var rulesInDb = await db.DynamicPricingRules.Where(r => r.SessionId == session.SessionId).ToListAsync();
        Assert.Single(rulesInDb);
        Assert.Equal("早鸟调价规则", rulesInDb[0].RuleName);
        Assert.Equal(0.75m, rulesInDb[0].AdjustmentValue);
    }

    [Fact]
    public async Task ConfigureDynamicPricingRules_WhenExceptionOccurs_RollsBackTransaction()
    {
        // 使用 SQLite In-Memory 数据库支持真实 DB 事务回滚校验
        using var connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False;");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite(connection)
        .ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCustomizer, SqliteTestModelCustomizer>() // ✅ 关键新增
        .Options;

        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var session = SeedShowSession(db, 1, 10);

        db.DynamicPricingRules.Add(new DynamicPricingRule
        {
            SessionId = session.SessionId,
            RuleName = "初始规则",
            TriggerType = "TIME_WINDOW",
            AdjustmentType = "AMOUNT_OFF",
            AdjustmentValue = 20m,
            Status = "ENABLED",
            CreateBy = "admin",
            UpdateBy = "admin",
            CreateTime = DateTime.UtcNow,
            UpdateTime = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = CreateAdminController(db);

        // 构造非法调价时间窗口（StartOffset 10 < EndOffset 30），在 Service 校验层抛出 ArgumentException 并触发展示回滚
        var invalidRequests = new[]
        {
            new CreateDynamicPricingRuleRequest(
                SeatSectionId: 1,
                RuleName: "非法规则",
                TriggerType: "TIME_WINDOW",
                StartOffsetMinutes: 10,
                EndOffsetMinutes: 30,
                AdjustmentType: "DISCOUNT_RATE",
                AdjustmentValue: 0.8m,
                Priority: 1)
        };

        var actionResult = await controller.ConfigureDynamicPricingRules(session.SessionId, invalidRequests, CancellationToken.None);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<object>>(badRequestResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Equal("INVALID_ARGUMENT", apiResponse.Code);

        // 断言数据库已完全回滚：初始规则未被 Remove，亦未插入新规则
        var rulesInDb = await db.DynamicPricingRules
            .Where(r => r.SessionId == session.SessionId)
            .ToListAsync();

        Assert.Single(rulesInDb);
        Assert.Equal("初始规则", rulesInDb[0].RuleName);
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

// 主键类型在 SQLite 中不能使用 AUTOINCREMENT，如果在模型中硬编码了主键类型（NUMBER(19,0)），可能会导致 SQLite 报错。
internal sealed class SqliteTestModelCustomizer : Microsoft.EntityFrameworkCore.Infrastructure.ModelCustomizer
{
    public SqliteTestModelCustomizer(Microsoft.EntityFrameworkCore.Infrastructure.ModelCustomizerDependencies dependencies)
        : base(dependencies) { }

    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                property.SetColumnType(null); // 清空主键硬编码类型，避免 SQLite 报 AUTOINCREMENT 错误
            }
        }
    }
}
