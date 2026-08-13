using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Common;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.ShowSessionChange;
using ShowtimeBackend.DTOs.ShowSessionDto;
using ShowtimeBackend.Entities.ShowSession;
using ShowtimeBackend.Services.ShowSession;

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
        var sessions = await _context.ShowSessions
            .AsNoTracking()
            .Where(s => s.ShowId == showId && s.SessionStatus == SessionStatus.ONSALE.ToDbString())
            .OrderBy(s => s.StartTime)
            .ToListAsync(cancellationToken);

        return sessions.Select(ToDto);
    }

    public async Task<IEnumerable<PricingStrategyDto>> GetPricingStrategiesAsync(
        long sessionId,
        CancellationToken cancellationToken = default)
    {
        var strategies = await _context.PriceStrategy
            .AsNoTracking()
            .Where(p => p.SessionId == sessionId && p.Status == PriceStrategyStatus.ENABLED.ToDbString())
            .OrderBy(p => p.SeatSectionId)
            .ToListAsync(cancellationToken);

        return strategies.Select(p => new PricingStrategyDto(
            p.PriceStrategyId,
            p.SeatSectionId,
            p.PriceType.ToEnum<PriceType>(),
            p.Price,
            p.Status.ToEnum<PriceStrategyStatus>()));
    }

    internal static ShowSessionDto ToDto(ShowtimeBackend.Entities.ShowSession.ShowSession s) => new(
        s.ShowId,
        s.SessionId,
        s.StartTime,
        s.EndTime,
        s.SaleStartTime,
        s.SaleEndTime,
        s.SessionStatus.ToEnum<SessionStatus>(),
        s.SeatMapId
    );
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
        if (request.StartTime >= request.EndTime)
            throw new ArgumentException("演出结束时间必须晚于开始时间");

        if (request.SaleStartTime >= request.SaleEndTime)
            throw new ArgumentException("预售结束时间必须晚于预售开始时间");

        // 区间重叠防排期冲突算法
        bool hasConflict = await _context.ShowSessions.CountAsync(s =>
            s.SeatMapId == request.SeatMapId &&
            s.SessionStatus != SessionStatus.ENDED.ToDbString() &&
            request.StartTime < s.EndTime && request.EndTime > s.StartTime,
            cancellationToken) > 0;

        if (hasConflict)
            throw new InvalidOperationException("该场地在指定时间段内已存在其他场次排期");

        var sessionEntity = new ShowtimeBackend.Entities.ShowSession.ShowSession
        {
            ShowId = showId,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            SaleStartTime = request.SaleStartTime,
            SaleEndTime = request.SaleEndTime,
            SeatMapId = request.SeatMapId,
            SessionStatus = SessionStatus.PRESALE.ToDbString(), // 严格与 DDL CK_SESSION_STATUS 对齐
            CreateTime = DateTime.UtcNow
        };

        _context.ShowSessions.Add(sessionEntity);
        await _context.SaveChangesAsync(cancellationToken);

        return ToDto(sessionEntity);
    }

    public async Task<bool> ConfigurePriceStrategiesAsync(
        long sessionId,
        IEnumerable<CreatePriceStrategyRequest> requests,
        CancellationToken cancellationToken = default)
    {
        var session = await _context.ShowSessions.FindAsync(new object[] { sessionId }, cancellationToken);
        if (session == null)
            throw new KeyNotFoundException($"未找到ID为 {sessionId} 的场次");

        var requestList = requests.ToList();
        if (!requestList.Any())
            throw new ArgumentException("票价策略不能为空");

        // PriceType 已由 DTO 枚举 + JSON 模型绑定保证合法（取值与 DDL CK_PRICE_TYPE 一致）

        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // 清理旧策略
            var oldStrategies = await _context.PriceStrategy
                .Where(p => p.SessionId == sessionId)
                .ToListAsync(cancellationToken);
            _context.PriceStrategy.RemoveRange(oldStrategies);

            // 构建新策略实体，补齐所有 NOT NULL 字段
            var newEntities = requestList.Select(r => new PriceStrategy
            {
                SessionId = sessionId,
                SeatSectionId = r.SeatSectionId,
                StrategyName = string.IsNullOrWhiteSpace(r.StrategyName)
                    ? $"{r.PriceType}_STRATEGY"
                    : r.StrategyName,
                PriceType = r.PriceType.ToDbString(),
                Price = r.Price,
                SaleStartTime = r.SaleStartTime ?? session.SaleStartTime,
                SaleEndTime = r.SaleEndTime ?? session.SaleEndTime,
                Priority = r.Priority,
                Quota = r.Quota,
                Status = PriceStrategyStatus.ENABLED.ToDbString(),
                CreateTime = DateTime.UtcNow
            }).ToList();

            _context.PriceStrategy.AddRange(newEntities);
            session.UpdateTime = DateTime.UtcNow;

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
        SessionStatus newStatus,
        CancellationToken cancellationToken = default)
    {
        var session = await _context.ShowSessions.FindAsync(new object[] { sessionId }, cancellationToken);
        if (session == null)
            throw new KeyNotFoundException($"未找到 ID 为 {sessionId} 的场次");

        // 状态值已由 DTO 枚举 + JSON 模型绑定保证合法（取值与 DDL CK_SHOW_SESSION_STATUS 一致）
        session.SessionStatus = newStatus.ToDbString();
        session.UpdateTime = DateTime.UtcNow;

        _context.ShowSessions.Update(session);
        return await _context.SaveChangesAsync(cancellationToken) > 0;
    }

    public async Task<IEnumerable<ShowSessionDto>> GetAdminSessionsByShowIdAsync(
        long showId,
        CancellationToken cancellationToken = default)
    {
        var sessions = await _context.ShowSessions
            .AsNoTracking()
            .Where(s => s.ShowId == showId)
            .OrderByDescending(s => s.StartTime)
            .ToListAsync(cancellationToken);

        return sessions.Select(ShowSessionService.ToDto);
    }

    internal static ShowSessionDto ToDto(ShowtimeBackend.Entities.ShowSession.ShowSession s) => new(
        s.ShowId,
        s.SessionId,
        s.StartTime,
        s.EndTime,
        s.SaleStartTime,
        s.SaleEndTime,
        s.SessionStatus.ToEnum<SessionStatus>(),
        s.SeatMapId
    );
}
