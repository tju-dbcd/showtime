using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Common;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.MarketingContent;
using ShowtimeBackend.Entities.ShowSession;
using ShowtimeBackend.Services.MarketingContent;

namespace ShowtimeBackend.Services.Impl;

public class AdminMarketingContentService : IAdminMarketingContentService
{
    private readonly AppDbContext _context;

    public AdminMarketingContentService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<MarketingContentDto> CreateContentAsync(
        CreateMarketingContentRequest request,
        string operatorName = "admin",
        CancellationToken cancellationToken = default)
    {
        // 校验关联演出是否存在 (用 CountAsync > 0 规避 Oracle EF Provider 的 Any 支持问题)
        bool showExists = await _context.Shows
            .AsNoTracking()
            .CountAsync(s => s.ShowId == request.ShowId, cancellationToken) > 0;

        if (!showExists)
            throw new ArgumentException($"未找到 ID 为 {request.ShowId} 的演出");

        var currentOperator = string.IsNullOrWhiteSpace(operatorName) ? "admin" : operatorName;
        var now = DateTime.UtcNow;

        var entity = new Entities.ShowSession.MarketingContent
        {
            ShowId = request.ShowId,
            ContentType = request.ContentType.ToDbString(),
            Title = request.Title,
            ContentText = request.ContentText,
            ImageUrl = request.ImageUrl,
            SortOrder = request.SortOrder,
            Status = request.Status.ToDbString(),
            PublishTime = request.PublishTime,
            CreateBy = currentOperator,
            UpdateBy = currentOperator,
            CreateTime = now,
            UpdateTime = now
        };

        _context.MarketingContents.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);

        return MapToDto(entity);
    }

    public async Task<bool> UpdateContentAsync(
        long contentId,
        UpdateMarketingContentRequest request,
        string operatorName = "admin",
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.MarketingContents
        .FirstOrDefaultAsync(m => m.ContentId == contentId, cancellationToken);
        if (entity == null)
            throw new KeyNotFoundException($"未找到 ID 为 {contentId} 的营销内容");

        var currentOperator = string.IsNullOrWhiteSpace(operatorName) ? "admin" : operatorName;

        entity.ContentType = request.ContentType.ToDbString();
        entity.Title = request.Title;
        entity.ContentText = request.ContentText;
        entity.ImageUrl = request.ImageUrl;
        entity.SortOrder = request.SortOrder;
        entity.Status = request.Status.ToDbString();
        entity.PublishTime = request.PublishTime;
        entity.UpdateBy = currentOperator;
        entity.UpdateTime = DateTime.UtcNow;

        _context.MarketingContents.Update(entity);
        return await _context.SaveChangesAsync(cancellationToken) > 0;
    }

    public async Task<bool> DeleteContentAsync(long contentId, CancellationToken cancellationToken = default)
    {
        var entity = await _context.MarketingContents.FindAsync(new object[] { contentId }, cancellationToken);
        if (entity == null)
            throw new KeyNotFoundException($"未找到 ID 为 {contentId} 的营销内容");

        _context.MarketingContents.Remove(entity);
        return await _context.SaveChangesAsync(cancellationToken) > 0;
    }

    public async Task<MarketingContentDto> GetContentByIdAsync(long contentId, CancellationToken cancellationToken = default)
    {
        var entity = await _context.MarketingContents
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.ContentId == contentId, cancellationToken);

        if (entity == null)
            throw new KeyNotFoundException($"未找到 ID 为 {contentId} 的营销内容");

        return MapToDto(entity);
    }

    public async Task<PagedResponse<MarketingContentDto>> GetContentsAsync(
        MarketingContentQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var dbQuery = _context.MarketingContents.AsNoTracking();

        if (query.ShowId.HasValue && query.ShowId > 0)
            dbQuery = dbQuery.Where(m => m.ShowId == query.ShowId.Value);

        if (query.ContentType.HasValue)
            dbQuery = dbQuery.Where(m => m.ContentType == query.ContentType.Value.ToDbString());

        if (query.Status.HasValue)
            dbQuery = dbQuery.Where(m => m.Status == query.Status.Value.ToDbString());

        if (!string.IsNullOrWhiteSpace(query.Keyword))
            dbQuery = dbQuery.Where(m => m.Title.Contains(query.Keyword));

        int total = await dbQuery.CountAsync(cancellationToken);

        int pageIndex = query.PageIndex < 1 ? 1 : query.PageIndex;
        int pageSize = query.PageSize < 1 ? 10 : query.PageSize;

        var items = await dbQuery
            .OrderBy(m => m.SortOrder)
            .ThenByDescending(m => m.CreateTime)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResponse<MarketingContentDto>(
            items.Select(MapToDto).ToList(),
            pageIndex,
            pageSize,
            total);
    }

    internal static MarketingContentDto MapToDto(Entities.ShowSession.MarketingContent m) => new(
        m.ContentId,
        m.ShowId,
        m.ContentType.ToEnum<MarketingContentType>(),
        m.Title,
        m.ContentText,
        m.ImageUrl,
        m.SortOrder,
        m.Status.ToEnum<MarketingContentStatus>(),
        m.PublishTime,
        m.CreateTime
    );
}

public class ClientMarketingContentService : IClientMarketingContentService
{
    private readonly AppDbContext _context;
    private readonly TimeProvider _timeProvider;

    public ClientMarketingContentService(AppDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<IEnumerable<MarketingContentDto>> GetClientContentsByShowIdAsync(
        long showId,
        MarketingContentType? contentType = null,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var enabledStatus = MarketingContentStatus.ENABLED.ToDbString();

        var query = _context.MarketingContents
            .AsNoTracking()
            .Where(m => m.ShowId == showId && m.Status == enabledStatus)
            .Where(m => m.PublishTime == null || m.PublishTime <= now);

        if (contentType.HasValue)
        {
            query = query.Where(m => m.ContentType == contentType.Value.ToDbString());
        }

        var list = await query
            .OrderBy(m => m.SortOrder)
            .ThenByDescending(m => m.PublishTime)
            .ToListAsync(cancellationToken);

        return list.Select(AdminMarketingContentService.MapToDto);
    }
}
