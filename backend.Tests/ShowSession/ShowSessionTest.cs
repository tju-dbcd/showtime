using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ShowtimeBackend.Common;
using ShowtimeBackend.Controllers.ShowSession.Admin;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.Show;
using ShowtimeBackend.Entities.ShowSession;
using ShowtimeBackend.Services.Impl;

namespace ShowtimeBackend.Tests.ShowSessionTest;

public sealed class AdminShowControllerTests
{
    [Fact]
    public async Task CreateShow_WithValidRequest_ReturnsCreatedAndPersistsWithPendingAuditStatus()
    {
        await using var db = CreateDbContext();
        var controller = CreateAdminController(db);
        var request = new CreateShowRequest("测试演出", 1, "演出简介", 120, "http://image.com/poster.jpg");

        var actionResult = await controller.CreateShow(request, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<ShowDto>>(createdResult.Value);
        Assert.True(apiResponse.Success);
        Assert.NotNull(apiResponse.Data);
        Assert.Equal("测试演出", apiResponse.Data.ShowName);
        Assert.Equal("PENDING", apiResponse.Data.AuditStatus); // 验证初始状态

        var dbShow = await db.Shows.FirstOrDefaultAsync(s => s.ShowId == apiResponse.Data.ShowId);
        Assert.NotNull(dbShow);
        Assert.Equal("PENDING", dbShow.AuditStatus);
    }

    [Fact]
    public async Task CreateShow_WhenNameIsEmpty_ReturnsBadRequest()
    {
        await using var db = CreateDbContext();
        var controller = CreateAdminController(db);
        var invalidRequest = new CreateShowRequest("", 1, "演出简介", 120, "http://image.com/poster.jpg");

        var actionResult = await controller.CreateShow(invalidRequest, CancellationToken.None);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<ShowDto>>(badRequestResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Equal("INVALID_ARGUMENT", apiResponse.Code);
    }

    [Fact]
    public async Task GetShows_WithPagination_ReturnsCorrectPagedData()
    {
        await using var db = CreateDbContext();
        db.Shows.AddRange(
            CreateShowEntity("演出 1", "DRAFT"),
            CreateShowEntity("演出 2", "PUBLISHED"),
            CreateShowEntity("演出 3", "PUBLISHED")
        );
        await db.SaveChangesAsync();

        var controller = CreateAdminController(db);
        var query = new ShowQueryRequest
        {
            PageIndex = 1,
            PageSize = 2
        };

        var actionResult = await controller.GetShows(query, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<PagedResponse<ShowDto>>>(okResult.Value);
        Assert.True(apiResponse.Success);

        var pagedResponse = apiResponse.Data!;
        Assert.Equal(1, pagedResponse.Page);        // Page
        Assert.Equal(2, pagedResponse.PageSize);    // PageSize
        Assert.Equal(3, pagedResponse.TotalCount);  // TotalCount
        Assert.Equal(2, pagedResponse.Items.Count); // Items 分页拦截数量
    }

    [Fact]
    public async Task GetShowById_WhenShowExists_ReturnsOk()
    {
        await using var db = CreateDbContext();
        var show = CreateShowEntity("特定演出", "PUBLISHED");
        db.Shows.Add(show);
        await db.SaveChangesAsync();

        var controller = CreateAdminController(db);

        var actionResult = await controller.GetShowById(show.ShowId, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<ShowDto>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Equal("特定演出", apiResponse.Data!.ShowName);
    }

    [Fact]
    public async Task GetShowById_WhenShowNotExist_ReturnsNotFound()
    {
        await using var db = CreateDbContext();
        var controller = CreateAdminController(db);

        var actionResult = await controller.GetShowById(999, CancellationToken.None);

        var notFoundResult = Assert.IsType<NotFoundObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<ShowDto>>(notFoundResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Equal("NOT_FOUND", apiResponse.Code);
    }

    [Fact]
    public async Task UpdateShow_WhenValidRequest_UpdatesDatabase()
    {
        await using var db = CreateDbContext();
        var show = CreateShowEntity("旧名字", "DRAFT");
        db.Shows.Add(show);
        await db.SaveChangesAsync();

        var controller = CreateAdminController(db);
        var updateRequest = new UpdateShowRequest("新名字", 2, "新描述", 150, "http://newposter.jpg", "PUBLISHED");

        var actionResult = await controller.UpdateShow(show.ShowId, updateRequest, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<object>>(okResult.Value);
        Assert.True(apiResponse.Success);

        var dbShow = await db.Shows.FindAsync(show.ShowId);
        Assert.Equal("新名字", dbShow!.ShowName);
        Assert.Equal("PUBLISHED", dbShow.Status);
    }

    [Fact]
    public async Task DeleteShow_WhenShowHasSessions_ReturnsConflict()
    {
        await using var db = CreateDbContext();
        var show = CreateShowEntity("有场次的演出", "PUBLISHED");
        db.Shows.Add(show);
        await db.SaveChangesAsync();

        // 添加关联场次
        db.ShowSessions.Add(new ShowSession
        {
            ShowId = show.ShowId,
            SeatMapId = 1,
            StartTime = DateTime.UtcNow.AddDays(1),
            EndTime = DateTime.UtcNow.AddDays(1).AddHours(2),
            SaleStartTime = DateTime.UtcNow,
            SaleEndTime = DateTime.UtcNow.AddDays(1),
            SessionStatus = "ONSALE"
        });
        await db.SaveChangesAsync();

        var controller = CreateAdminController(db);

        var actionResult = await controller.DeleteShow(show.ShowId, CancellationToken.None);

        var conflictResult = Assert.IsType<ConflictObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<object>>(conflictResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Equal("OPERATION_CONFLICT", apiResponse.Code);
    }

    [Fact]
    public async Task DeleteShow_WhenNoSessions_RemovesFromDatabase()
    {
        await using var db = CreateDbContext();
        var show = CreateShowEntity("待删除演出", "DRAFT");
        db.Shows.Add(show);
        await db.SaveChangesAsync();

        var controller = CreateAdminController(db);

        var actionResult = await controller.DeleteShow(show.ShowId, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<object>>(okResult.Value);
        Assert.True(apiResponse.Success);

        var dbShow = await db.Shows.FindAsync(show.ShowId);
        Assert.Null(dbShow);
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

    private static AdminShowController CreateAdminController(AppDbContext db)
    {
        var adminService = new AdminShowService(db);
        return new AdminShowController(adminService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = CreateAdminClaimsPrincipal()
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

    private static Show CreateShowEntity(string showName, string status)
    {
        return new Show
        {
            ShowName = showName,
            CategoryId = 1,
            Description = "测试描述",
            DurationMinutes = 100,
            PosterUrl = "http://test.com/poster.jpg",
            Status = status,
            AuditStatus = "APPROVED",
            CreateTime = DateTime.UtcNow
        };
    }
}
