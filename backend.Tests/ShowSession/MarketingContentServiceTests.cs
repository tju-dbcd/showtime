using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.MarketingContent;
using ShowtimeBackend.Entities.ShowSession;
using ShowtimeBackend.Services.Impl;

namespace ShowtimeBackend.Tests.ShowSessionTest;

// 营销内容最小单测：覆盖 Admin CRUD 主路径/关键分支 与 Client 端过滤逻辑。
// 使用 EF InMemory（不校验外键），服务内“演出是否存在”的校验通过预置 Show 行满足。
public sealed class MarketingContentServiceTests
{
    [Fact]
    public async Task CreateContent_WhenShowExists_ReturnsDtoAndPersists()
    {
        await using var db = CreateDbContext();
        var show = await SeedShowAsync(db);
        var service = new AdminMarketingContentService(db);

        var created = await service.CreateContentAsync(
            CreateRequest(show.ShowId, MarketingContentType.NOTICE, title: "停演公告", imageUrl: "http://example.com/a.png"));

        Assert.True(created.ContentId > 0);
        Assert.Equal(show.ShowId, created.ShowId);
        Assert.Equal(MarketingContentType.NOTICE, created.ContentType);
        Assert.Equal("停演公告", created.Title);
        Assert.Equal("http://example.com/a.png", created.ImageUrl);
        Assert.Equal(MarketingContentStatus.ENABLED, created.Status);

        var inDb = await db.MarketingContents.SingleAsync(m => m.ContentId == created.ContentId);
        Assert.Equal("NOTICE", inDb.ContentType);
        Assert.Equal("ENABLED", inDb.Status);
    }

    [Fact]
    public async Task CreateContent_WhenShowMissing_Throws()
    {
        await using var db = CreateDbContext();
        var service = new AdminMarketingContentService(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateContentAsync(CreateRequest(9999, MarketingContentType.NOTICE)));
    }

    [Fact]
    public async Task AdminGetContents_ByShowIdAndKeyword_Filters()
    {
        await using var db = CreateDbContext();
        var showA = await SeedShowAsync(db);
        var showB = await SeedShowAsync(db);

        await SeedContentAsync(db, showA.ShowId, MarketingContentType.NOTICE, "甲公告", 1);
        await SeedContentAsync(db, showA.ShowId, MarketingContentType.AD, "乙广告", 2);
        await SeedContentAsync(db, showB.ShowId, MarketingContentType.NOTICE, "丙公告", 3);

        var service = new AdminMarketingContentService(db);

        var onlyA = await service.GetContentsAsync(
            new MarketingContentQueryRequest(ShowId: showA.ShowId, PageIndex: 1, PageSize: 10),
            CancellationToken.None);
        Assert.Equal(2, onlyA.TotalCount);

        var keyworded = await service.GetContentsAsync(
            new MarketingContentQueryRequest(ShowId: showA.ShowId, Keyword: "广告", PageIndex: 1, PageSize: 10),
            CancellationToken.None);
        Assert.Equal(1, keyworded.TotalCount);
        Assert.Equal("乙广告", keyworded.Items[0].Title);
    }

    [Fact]
    public async Task AdminUpdateAndDelete_WorkOnExistingContent()
    {
        await using var db = CreateDbContext();
        var show = await SeedShowAsync(db);
        var content = await SeedContentAsync(db, show.ShowId, MarketingContentType.NOTICE, "原标题", 1);

        var service = new AdminMarketingContentService(db);

        var updated = await service.UpdateContentAsync(
            content.ContentId,
            new UpdateMarketingContentRequest(
                ContentType: MarketingContentType.PROMOTION,
                Title: "新标题",
                ContentText: "正文",
                ImageUrl: "/files/showtime-uploads/promo.png",
                SortOrder: 3,
                Status: MarketingContentStatus.ENABLED,
                PublishTime: DateTime.UtcNow),
            "admin",
            CancellationToken.None);
        Assert.True(updated);

        var fetched = await service.GetContentByIdAsync(content.ContentId, CancellationToken.None);
        Assert.Equal("新标题", fetched.Title);
        Assert.Equal(MarketingContentType.PROMOTION, fetched.ContentType);

        var deleted = await service.DeleteContentAsync(content.ContentId, CancellationToken.None);
        Assert.True(deleted);
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.GetContentByIdAsync(content.ContentId, CancellationToken.None));
    }

    [Fact]
    public async Task ClientGetContents_ReturnsOnlyEnabledPublished_AndFiltersByType()
    {
        await using var db = CreateDbContext();
        var show = await SeedShowAsync(db);

        // 生效：ENABLED 且已到发布时间
        await SeedContentAsync(db, show.ShowId, MarketingContentType.NOTICE, "有效公告",
            sort: 1, status: "ENABLED", publishTime: DateTime.UtcNow.AddDays(-1));
        // 生效：ENABLED 且 PublishTime 为空（立即生效）
        await SeedContentAsync(db, show.ShowId, MarketingContentType.AD, "有效广告",
            sort: 2, status: "ENABLED", publishTime: null);
        // 不生效：DISABLED
        await SeedContentAsync(db, show.ShowId, MarketingContentType.PROMOTION, "禁用促销",
            sort: 3, status: "DISABLED", publishTime: DateTime.UtcNow.AddDays(-1));
        // 不生效：尚未到发布时间
        await SeedContentAsync(db, show.ShowId, MarketingContentType.AD, "未发布广告",
            sort: 4, status: "ENABLED", publishTime: DateTime.UtcNow.AddDays(1));
        // 其它演出数据不应串台
        var otherShow = await SeedShowAsync(db);
        await SeedContentAsync(db, otherShow.ShowId, MarketingContentType.AD, "别的演出", 5, "ENABLED", DateTime.UtcNow.AddDays(-1));

        var service = new ClientMarketingContentService(db, TimeProvider.System);

        var all = await service.GetClientContentsByShowIdAsync(show.ShowId, null, CancellationToken.None);
        var titles = all.Select(c => c.Title).ToList();
        Assert.Equal(2, all.Count());
        Assert.Contains("有效公告", titles);
        Assert.Contains("有效广告", titles);
        Assert.DoesNotContain("禁用促销", titles);
        Assert.DoesNotContain("未发布广告", titles);
        Assert.DoesNotContain("别的演出", titles);

        var ads = await service.GetClientContentsByShowIdAsync(show.ShowId, MarketingContentType.AD, CancellationToken.None);
        Assert.Single(ads);
        Assert.Equal("有效广告", ads.Single().Title);
    }

    // ====================== Helpers ======================

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static async Task<Show> SeedShowAsync(AppDbContext db)
    {
        var now = DateTime.UtcNow;
        var show = new Show
        {
            ShowName = $"测试演出-{Guid.NewGuid():N}",
            CategoryId = 1,
            Status = "PUBLISHED",
            AuditStatus = "APPROVED",
            CreateBy = "admin",
            UpdateBy = "admin",
            CreateTime = now,
            UpdateTime = now
        };
        db.Shows.Add(show);
        await db.SaveChangesAsync();
        return show;
    }

    private static async Task<Entities.ShowSession.MarketingContent> SeedContentAsync(
        AppDbContext db,
        long showId,
        MarketingContentType type,
        string title,
        int sort,
        string status = "ENABLED",
        DateTime? publishTime = null)
    {
        var now = DateTime.UtcNow;
        var content = new Entities.ShowSession.MarketingContent
        {
            ShowId = showId,
            ContentType = type.ToString(),
            Title = title,
            ContentText = null,
            ImageUrl = "http://example.com/x.png",
            SortOrder = sort,
            Status = status,
            PublishTime = publishTime,
            CreateBy = "admin",
            UpdateBy = "admin",
            CreateTime = now,
            UpdateTime = now
        };
        db.MarketingContents.Add(content);
        await db.SaveChangesAsync();
        return content;
    }

    private static CreateMarketingContentRequest CreateRequest(
        long showId,
        MarketingContentType type,
        string title = "标题",
        string? imageUrl = null)
        => new(
            ShowId: showId,
            ContentType: type,
            Title: title,
            ContentText: "正文",
            ImageUrl: imageUrl,
            SortOrder: 0,
            Status: MarketingContentStatus.ENABLED,
            PublishTime: null);
}
