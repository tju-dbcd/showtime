using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Data;
using ShowtimeBackend.Entities.ShowSession; 
using ShowtimeBackend.Dtos.Client;
using ShowtimeBackend.Dtos.Admin;
using ShowtimeBackend.Services.Interfaces;

namespace ShowtimeBackend.Services.Impl;

public class ShowSessionService : IClientShowSessionService
{
    private readonly AppDbContext _context;

    public ShowSessionService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<ShowSessionDto>> GetOnSaleSessionsAsync(
        long showId,
        CancellationToken cancellationToken = default)
    {
        // LINQ 条件顺序严格匹配 DDL 索引 IDX_SHOW_SESSION_SHOW_STATUS (SHOW_ID, SESSION_STATUS, START_TIME)
        return await _context.ShowSessions
            .AsNoTracking() // 禁用状态追踪，提升高并发读取性能
            .Where(s => s.ShowId == showId && s.SessionStatus == "ONSALE") // 最左前缀匹配
            .OrderBy(s => s.StartTime)                                      // 利用索引自带的排序特征
            .Select(s => new ShowSessionDto(                             // DTO 投影，精前 SQL 选取列
                s.ShowId,
                s.SessionId,
                s.StartTime,
                s.EndTime,
                s.SaleStartTime,
                s.SessionStatus
                //s.MinPrice TODO：在原有设计中没有定义该内容，需要后续设定修改
            ))
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<PricingStrategyDto>> GetPricingStrategiesAsync(
        long sessionId,
        CancellationToken cancellationToken = default)
    {
        // 匹配 DDL 索引 IDX_PRICE_STRATEGY_SESS_SEC (SESSION_ID, SEAT_SECTION_ID, STATUS)
        return await _context.PriceStrategies
            .AsNoTracking()
            .Where(p => p.SessionId == sessionId && p.Status == "ENABLED")
            .OrderBy(p => p.SeatSectionId)
            .Select(p => new PricingStrategyDto(
                p.PriceStrategyId,
                p.SeatSectionId,
                p.PriceType,
                p.Price,
                p.Status
            ))
            .ToListAsync(cancellationToken);
    }
}

public class AdminShowSessionService : IAdminShowSessionService
{
    private readonly AppDbContext _context;

    public AdminShowSessionService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<ShowSessionDto> CreateSessionAsync(
        long showId,
        CreateShowSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        // 基础时间校验
        if (request.StartTime >= request.EndTime)
            throw new ArgumentException("演出结束时间必须晚于开始时间");

        if (request.SaleStartTime >= request.SaleEndTime)
            throw new ArgumentException("预售结束时间必须晚于预售开始时间");

        // 防重排期校验：同影厅/场馆在相同时间段内不能有重叠场次
        bool hasConflict = await _context.ShowSessions.AnyAsync(s =>
            s.SeatMapId == request.SeatMapId &&
            s.SessionStatus != "CLOSED" &&
            ((request.StartTime >= s.StartTime && request.StartTime < s.EndTime) ||
             (request.EndTime > s.StartTime && request.EndTime <= s.EndTime)),
            cancellationToken);

        if (hasConflict)
            throw new InvalidOperationException("该场地在指定时间段内已存在其他场次排期");

        // 构建实体并保存
        var sessionEntity = new ShowSession
        {
            ShowId = showId,
            SessionId = request.SessionId,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            SaleStartTime = request.SaleStartTime,
            SaleEndTime = request.SaleEndTime,
            SeatMapId = request.SeatMapId,
            SessionStatus = "PRE_SALE", // 默认新场次为预售状态
            //MinPrice = 0m,             
            CreateTime = DateTime.UtcNow
        };

        _context.ShowSessions.Add(sessionEntity);
        await _context.SaveChangesAsync(cancellationToken);

        return new ShowSessionDto(
            sessionEntity.ShowId,
            sessionEntity.SessionId,
            sessionEntity.StartTime,
            sessionEntity.EndTime,
            sessionEntity.SaleStartTime,
            sessionEntity.SessionStatus
            //sessionEntity.MinPrice
        );
    }

    public async Task<bool> ConfigurePriceStrategiesAsync(
        long sessionId,
        IEnumerable<CreatePriceStrategyRequest> requests,
        CancellationToken cancellationToken = default)
    {
        var session = await _context.ShowSessions.FindAsync(new object[] { sessionId }, cancellationToken);
        if (session == null)
            throw new KeyNotFoundException($"未找到 ID 为 {sessionId} 的场次");

        var requestList = requests.ToList();
        if (!requestList.Any())
            throw new ArgumentException("票价策略不能为空");

        // 开启显式事务，确保清空旧策略与插入新策略原子生效
        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // 删除该场次旧的策略（覆盖更新）
            var oldStrategies = await _context.PriceStrategies
                .Where(p => p.SessionId == sessionId)
                .ToListAsync(cancellationToken);
            _context.PriceStrategies.RemoveRange(oldStrategies);

            // 批量构建新策略实体
            var newEntities = requestList.Select(r => new PriceStrategy
            {
                SessionId = sessionId,
                SeatSectionId = r.SeatSectionId,
                PriceType = r.PriceType,
                Price = r.Price,
                Status = "ENABLED",
                CreateTime = DateTime.UtcNow
            }).ToList();

            _context.PriceStrategies.AddRange(newEntities);
            _context.ShowSessions.Update(session);

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return true;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<bool> UpdateSessionStatusAsync(
        long sessionId,
        string newStatus,
        CancellationToken cancellationToken = default)
    {
        var session = await _context.ShowSessions.FindAsync(new object[] { sessionId }, cancellationToken);
        if (session == null)
            throw new KeyNotFoundException($"未找到 ID 为 {sessionId} 的场次");

        // 状态校验
        var validStatuses = new[] { "PRE_SALE", "ONSALE", "SUSPENDED", "CLOSED" };
        if (!validStatuses.Contains(newStatus))
            throw new ArgumentException("不合法的场次状态");

        session.SessionStatus = newStatus;
        session.UpdateTime = DateTime.UtcNow;

        _context.ShowSessions.Update(session);
        return await _context.SaveChangesAsync(cancellationToken) > 0;
    }
}
