using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.Show;
using ShowtimeBackend.Services.ShowSession;
using ShowtimeBackend.Entities.ShowSession;
using ShowtimeBackend.Common;

namespace ShowtimeBackend.Services.Impl;

public class AdminShowService : IAdminShowService
{
    /// <summary>
    /// 演出状态取值白名单（与 SHOW.STATUS 的 CHECK 约束对齐）。
    /// </summary>
    private static readonly HashSet<string> ValidShowStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "DRAFT", "PUBLISHED", "UNPUBLISHED" };

    private readonly AppDbContext _context;

    public AdminShowService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<ShowDto> CreateShowAsync(CreateShowRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ShowName))
            throw new ArgumentException("演出名称不能为空");

        await ValidateCategoryExistsAsync(request.CategoryId, cancellationToken);

        var show = new ShowtimeBackend.Entities.ShowSession.Show
        {
            ShowName = request.ShowName,
            CategoryId = request.CategoryId,
            Description = request.Description,
            DurationMinutes = request.DurationMinutes,
            PosterUrl = request.PosterUrl,
            Status = "DRAFT",
            // <fix>新建演出初始化审核状态设为 PENDING
            AuditStatus = "PENDING"
        };

        _context.Shows.Add(show);
        await _context.SaveChangesAsync(cancellationToken);

        return MapToDto(show);
    }

    public async Task<bool> UpdateShowAsync(long showId, UpdateShowRequest request, CancellationToken cancellationToken = default)
    {
        var show = await _context.Shows.FindAsync(new object[] { showId }, cancellationToken);
        if (show == null)
            throw new KeyNotFoundException($"未找到 ID 为 {showId} 的演出");

        if (string.IsNullOrWhiteSpace(request.ShowName))
            throw new ArgumentException("演出名称不能为空");
        if (!ValidShowStatuses.Contains(request.Status))
            throw new ArgumentException($"无效的演出状态: {request.Status}");
        await ValidateCategoryExistsAsync(request.CategoryId, cancellationToken);

        show.ShowName = request.ShowName;
        show.CategoryId = request.CategoryId;
        show.Description = request.Description;
        show.DurationMinutes = request.DurationMinutes;
        show.PosterUrl = request.PosterUrl;
        show.Status = request.Status;
        //<fix> 已删除 show.UpdateTime = DateTime.UtcNow

        _context.Shows.Update(show);
        return await _context.SaveChangesAsync(cancellationToken) > 0;
    }

    public async Task<bool> DeleteShowAsync(long showId, CancellationToken cancellationToken = default)
    {
        var show = await _context.Shows.FindAsync(new object[] { showId }, cancellationToken);
        if (show == null)
            throw new KeyNotFoundException($"未找到 ID 为 {showId} 的演出");

        bool hasSessions = await _context.ShowSessions.AnyAsync(s => s.ShowId == showId, cancellationToken);
        if (hasSessions)
            throw new InvalidOperationException("该演出下已存在关联场次，无法直接删除");

        _context.Shows.Remove(show);
        return await _context.SaveChangesAsync(cancellationToken) > 0;
    }

    public async Task<ShowDto> GetShowByIdAsync(long showId, CancellationToken cancellationToken = default)
    {
        var show = await _context.Shows.AsNoTracking().FirstOrDefaultAsync(s => s.ShowId == showId, cancellationToken);
        if (show == null)
            throw new KeyNotFoundException($"未找到 ID 为 {showId} 的演出");

        return MapToDto(show);
    }

    public async Task<PagedResponse<ShowDto>> GetShowsAsync(ShowQueryRequest query, CancellationToken cancellationToken = default)
    {
        var dbQuery = _context.Shows.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Keyword))
            dbQuery = dbQuery.Where(s => s.ShowName.Contains(query.Keyword));

        if (query.CategoryId.HasValue && query.CategoryId > 0)
            dbQuery = dbQuery.Where(s => s.CategoryId == query.CategoryId.Value);

        if (!string.IsNullOrWhiteSpace(query.Status))
            dbQuery = dbQuery.Where(s => s.Status == query.Status);

        int total = await dbQuery.CountAsync(cancellationToken);

        // 与 C 端一致：对分页参数做防御性钳制，避免负值导致 Skip 抛异常
        int pageIndex = query.PageIndex < 1 ? 1 : query.PageIndex;
        int pageSize = query.PageSize < 1 ? 10 : query.PageSize;

        var items = await dbQuery
            .OrderByDescending(s => s.CreateTime)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(s => MapToDto(s))
            .ToListAsync(cancellationToken);

        // <fix> 修复构造参数顺序
        return new PagedResponse<ShowDto>(items, pageIndex, pageSize, total);
    }

    private async Task ValidateCategoryExistsAsync(long categoryId, CancellationToken cancellationToken)
    {
        bool exists = await _context.Set<ShowtimeBackend.Entities.ShowSession.Category>()
            .AsNoTracking()
            .AnyAsync(c => c.CategoryId == categoryId, cancellationToken);
        if (!exists)
            throw new ArgumentException($"未找到 ID 为 {categoryId} 的演出分类");
    }

    private static ShowDto MapToDto(ShowtimeBackend.Entities.ShowSession.Show show) => new(
        show.ShowId,
        show.ShowName,
        show.CategoryId,
        show.Description,
        show.DurationMinutes,
        show.PosterUrl,
        show.Status,
        show.AuditStatus,
        show.CreateTime
    );
}

public class ClientShowService : IClientShowService
{
    private readonly AppDbContext _context;

    public ClientShowService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResponse<ShowDto>> GetClientShowsAsync(ShowQueryRequest query, CancellationToken cancellationToken = default)
    {
        // 仅有非pending情况下可以被查询
        var dbQuery = _context.Shows
            .AsNoTracking()
            .Where(s => s.Status == "PUBLISHED" && s.AuditStatus == "APPROVED");

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            dbQuery = dbQuery.Where(s => s.ShowName.Contains(query.Keyword));
        }

        if (query.CategoryId.HasValue && query.CategoryId > 0)
        {
            dbQuery = dbQuery.Where(s => s.CategoryId == query.CategoryId.Value);
        }

        int total = await dbQuery.CountAsync(cancellationToken);

        int pageIndex = query.PageIndex < 1 ? 1 : query.PageIndex;
        int pageSize = query.PageSize < 1 ? 10 : query.PageSize;

        var items = await dbQuery
            .OrderByDescending(s => s.CreateTime)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(s => MapToDto(s))
            .ToListAsync(cancellationToken);

        return new PagedResponse<ShowDto>(items, pageIndex, pageSize, total);
    }

    public async Task<ShowDto> GetClientShowByIdAsync(long showId, CancellationToken cancellationToken = default)
    {
        var show = await _context.Shows
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ShowId == showId && s.Status == "PUBLISHED" && s.AuditStatus == "APPROVED", cancellationToken);

        if (show == null)
        {
            throw new KeyNotFoundException($"未找到 ID 为 {showId} 的有效演出或该演出未上架");
        }

        return MapToDto(show);
    }

    private static ShowDto MapToDto(Show show) => new(
        show.ShowId,
        show.ShowName,
        show.CategoryId,
        show.Description,
        show.DurationMinutes,
        show.PosterUrl,
        show.Status,
        show.AuditStatus,
        show.CreateTime
    );
}
