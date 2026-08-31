using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ShowtimeBackend.Common;
using ShowtimeBackend.Controllers.ShowSession.Admin;
using ShowtimeBackend.Controllers.ShowSession.Client;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.Show;
using ShowtimeBackend.Entities.ShowSession;
using ShowtimeBackend.Services.Impl;

namespace ShowtimeBackend.Tests.ShowAdmin;

/// <summary>
/// 管理端演出（Show）CRUD 接口与新增的业务校验单测。
/// </summary>
public sealed class ShowAdminControllerTests
{
    [Fact]
    public async Task CreateShow_WithValidRequest_ReturnsCreatedAndPersistsWithPendingAuditStatus()
    {
        await using var db = CreateAndSeedDbContext();
        var controller = CreateAdminController(db);
        var request = new CreateShowRequest("测试演出", 1, "演出简介", 120, "http://image.com/poster.jpg");

        var actionResult = await controller.CreateShow(request, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<ShowDto>>(createdResult.Value);
        Assert.True(apiResponse.Success);
        Assert.NotNull(apiResponse.Data);
        Assert.Equal("测试演出", apiResponse.Data.ShowName);
        Assert.Equal(ShowStatus.DRAFT, apiResponse.Data.Status);
        Assert.Equal(ShowAuditStatus.PENDING, apiResponse.Data.AuditStatus); // 验证初始审核状态

        var dbShow = await db.Shows.FirstOrDefaultAsync(s => s.ShowId == apiResponse.Data.ShowId);
        Assert.NotNull(dbShow);
        Assert.Equal("PENDING", dbShow.AuditStatus);
    }

    [Fact]
    public async Task CreateShow_WhenNameIsEmpty_ReturnsBadRequest()
    {
        await using var db = CreateAndSeedDbContext();
        var controller = CreateAdminController(db);
        var invalidRequest = new CreateShowRequest("", 1, "演出简介", 120, "http://image.com/poster.jpg");

        var actionResult = await controller.CreateShow(invalidRequest, CancellationToken.None);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<ShowDto>>(badRequestResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Equal("INVALID_ARGUMENT", apiResponse.Code);
    }

    [Fact]
    public async Task CreateShow_WhenCategoryNotExist_ReturnsBadRequest()
    {
        await using var db = CreateAndSeedDbContext();
        var controller = CreateAdminController(db);
        var request = new CreateShowRequest("分类缺失的演出", 999, "演出简介", 120, "http://image.com/poster.jpg");

        var actionResult = await controller.CreateShow(request, CancellationToken.None);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<ShowDto>>(badRequestResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Equal("INVALID_ARGUMENT", apiResponse.Code);
    }

    [Fact]
    public async Task GetShows_WithPagination_ReturnsCorrectPagedData()
    {
        await using var db = CreateAndSeedDbContext();
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
        Assert.Equal(2, pagedResponse.Items.Count); // 当前页条目数
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetShows_WithInvalidPageIndex_DoesNotThrow(int pageIndex)
    {
        await using var db = CreateAndSeedDbContext();
        db.Shows.AddRange(CreateShowEntity("演出 1", "PUBLISHED"));
        await db.SaveChangesAsync();

        var controller = CreateAdminController(db);
        var query = new ShowQueryRequest { PageIndex = pageIndex, PageSize = 10 };

        var actionResult = await controller.GetShows(query, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        Assert.True(Assert.IsType<ApiResponse<PagedResponse<ShowDto>>>(okResult.Value).Success);
    }

    [Fact]
    public async Task GetShows_WithStatusFilter_ReturnsOnlyMatchingShows()
    {
        await using var db = CreateAndSeedDbContext();
        db.Shows.AddRange(
            CreateShowEntity("草稿演出", "DRAFT"),
            CreateShowEntity("上架演出", "PUBLISHED")
        );
        await db.SaveChangesAsync();

        var controller = CreateAdminController(db);
        var query = new ShowQueryRequest { PageIndex = 1, PageSize = 10, Status = ShowStatus.PUBLISHED };

        var actionResult = await controller.GetShows(query, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<PagedResponse<ShowDto>>>(okResult.Value);
        var shows = apiResponse.Data!.Items;
        Assert.Single(shows);
        Assert.Equal(ShowStatus.PUBLISHED, shows.First().Status);
    }

    [Fact]
    public async Task GetShowById_WhenShowExists_ReturnsOk()
    {
        await using var db = CreateAndSeedDbContext();
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
        await using var db = CreateAndSeedDbContext();
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
        await using var db = CreateAndSeedDbContext();
        var show = CreateShowEntity("旧名字", "DRAFT");
        db.Shows.Add(show);
        await db.SaveChangesAsync();

        var controller = CreateAdminController(db);
        var updateRequest = new UpdateShowRequest("新名字", 2, "新描述", 150, "http://newposter.jpg", ShowStatus.PUBLISHED);

        var actionResult = await controller.UpdateShow(show.ShowId, updateRequest, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<object>>(okResult.Value);
        Assert.True(apiResponse.Success);

        var dbShow = await db.Shows.FindAsync(show.ShowId);
        Assert.Equal("新名字", dbShow!.ShowName);
        Assert.Equal("PUBLISHED", dbShow.Status);
        Assert.Equal(2, dbShow.CategoryId);
    }

    [Fact]
    public async Task UpdateShow_WhenShowNameEmpty_ReturnsBadRequest()
    {
        await using var db = CreateAndSeedDbContext();
        var show = CreateShowEntity("待更新", "DRAFT");
        db.Shows.Add(show);
        await db.SaveChangesAsync();

        var controller = CreateAdminController(db);
        var updateRequest = new UpdateShowRequest("", 1, "新描述", 150, "http://newposter.jpg", ShowStatus.PUBLISHED);

        var actionResult = await controller.UpdateShow(show.ShowId, updateRequest, CancellationToken.None);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<object>>(badRequestResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Equal("INVALID_ARGUMENT", apiResponse.Code);
    }

    [Fact]
    public async Task UpdateShow_WhenCategoryNotExist_ReturnsBadRequest()
    {
        await using var db = CreateAndSeedDbContext();
        var show = CreateShowEntity("待更新", "DRAFT");
        db.Shows.Add(show);
        await db.SaveChangesAsync();

        var controller = CreateAdminController(db);
        var updateRequest = new UpdateShowRequest("新名字", 999, "新描述", 150, "http://newposter.jpg", ShowStatus.PUBLISHED);

        var actionResult = await controller.UpdateShow(show.ShowId, updateRequest, CancellationToken.None);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<object>>(badRequestResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Equal("INVALID_ARGUMENT", apiResponse.Code);
    }

    [Fact]
    public async Task DeleteShow_WhenShowHasSessions_ReturnsConflict()
    {
        await using var db = CreateAndSeedDbContext();
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
        await using var db = CreateAndSeedDbContext();
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
    // C 端演出查询（ClientShowService）单测
    // ==========================================

    [Fact]
    public async Task GetClientShows_ReturnsOnlyPublishedAndApproved()
    {
        await using var db = CreateAndSeedDbContext();
        db.Shows.AddRange(
            CreateShowEntity("上架且审核通过", "PUBLISHED", "APPROVED"),
            CreateShowEntity("上架但待审核", "PUBLISHED", "PENDING"),
            CreateShowEntity("草稿状态", "DRAFT", "APPROVED"),
            CreateShowEntity("已下架", "UNPUBLISHED", "APPROVED")
        );
        await db.SaveChangesAsync();

        var controller = CreateClientController(db);
        var query = new ShowQueryRequest { PageIndex = 1, PageSize = 10 };

        var actionResult = await controller.GetShows(query, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<PagedResponse<ShowDto>>>(okResult.Value);
        var shows = apiResponse.Data!.Items;
        Assert.Single(shows);
        Assert.Equal("上架且审核通过", shows.First().ShowName);
    }

    [Fact]
    public async Task GetClientShowById_WhenNotPublished_ReturnsNotFound()
    {
        await using var db = CreateAndSeedDbContext();
        var show = CreateShowEntity("草稿演出", "DRAFT", "APPROVED");
        db.Shows.Add(show);
        await db.SaveChangesAsync();

        var controller = CreateClientController(db);

        var actionResult = await controller.GetShowById(show.ShowId, CancellationToken.None);

        var notFoundResult = Assert.IsType<NotFoundObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<ShowDto>>(notFoundResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Equal("NOT_FOUND", apiResponse.Code);
    }

    // ==========================================
    // Helper Methods
    // ==========================================

    private static AppDbContext CreateAndSeedDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var db = new AppDbContext(options);
        // 预置分类，避免 Create/Update 时 CategoryId 存在性校验失败
        db.Set<Category>().AddRange(
            new Category { CategoryId = 1, CategoryName = "音乐剧" },
            new Category { CategoryId = 2, CategoryName = "话剧" }
        );
        db.SaveChanges();
        return db;
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

    private static ShowSessionClientController CreateClientController(AppDbContext db)
    {
        // <fix> 补充 TimeProvider.System 
        var sessionService = new ShowSessionService(db, TimeProvider.System);
        var showService = new ClientShowService(db);
        return new ShowSessionClientController(sessionService, showService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
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

    private static ShowtimeBackend.Entities.ShowSession.Show CreateShowEntity(string showName, string status, string? auditStatus = null)
    {
        return new ShowtimeBackend.Entities.ShowSession.Show
        {
            ShowName = showName,
            CategoryId = 1,
            Description = "测试描述",
            DurationMinutes = 100,
            PosterUrl = "http://test.com/poster.jpg",
            Status = status,
            AuditStatus = auditStatus ?? "APPROVED",
            CreateTime = DateTime.UtcNow
        };
    }
}
