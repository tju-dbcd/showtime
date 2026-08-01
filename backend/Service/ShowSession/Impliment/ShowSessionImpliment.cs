using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Data;
using ShowtimeBackend.Dtos.Client;
using ShowtimeBackend.Services.Interfaces;

namespace ShowtimeBackend.Services.Impl;

public class ShowSessionService : IShowSessionService
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
