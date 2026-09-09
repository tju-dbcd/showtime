using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Common;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.ShowSessionChange;
using ShowtimeBackend.DTOs.ShowSessionDto;
using ShowtimeBackend.Entities.ShowSession;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Services.ShowSession;

namespace ShowtimeBackend.Services.Impl;

public static class ShowSessionExtensions
{
    public static IQueryable<ShowtimeBackend.Entities.ShowSession.ShowSession> WhereIsOnSale(this IQueryable<ShowtimeBackend.Entities.ShowSession.ShowSession> query, DateTime nowUtc)
    {
        var onSaleStatusStr = SessionStatus.ONSALE.ToDbString();
        return query.Where(s =>
            s.SessionStatus == onSaleStatusStr &&
            s.SaleStartTime <= nowUtc &&
            s.SaleEndTime >= nowUtc);
    }
}

public class ShowSessionService : IClientShowSessionService
{
    private readonly AppDbContext _context;
    private readonly TimeProvider _timeProvider;

    public ShowSessionService(AppDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<IEnumerable<ShowSessionDto>> GetOnSaleSessionsAsync(
        long showId,
        CancellationToken cancellationToken = default)
    {
        var sessions = await _context.ShowSessions
            .AsNoTracking()
            .Where(s => s.ShowId == showId && s.SessionStatus == SessionStatus.ONSALE.ToDbString())
            .WhereIsOnSale(_timeProvider.GetUtcNow().UtcDateTime)
            .OrderBy(s => s.StartTime)
            .ToListAsync(cancellationToken);

        return sessions.Select(ToDto);
    }

    /// <summary>
    /// 获取场次票价策略（前端展示价计算）
    /// </summary>
    public async Task<IEnumerable<PricingStrategyDto>> GetPricingStrategiesAsync(
        long sessionId,
        CancellationToken cancellationToken = default)
    {
        var strategies = await _context.PriceStrategy
            .AsNoTracking()
            .Where(p => p.SessionId == sessionId && p.Status == PriceStrategyStatus.ENABLED.ToDbString())
            .OrderBy(p => p.SeatSectionId)
            .ToListAsync(cancellationToken);

        if (!strategies.Any()) return [];

        var session = await _context.ShowSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, cancellationToken);

        var dynamicRules = await _context.DynamicPricingRules
            .AsNoTracking()
            .Where(r => r.SessionId == sessionId && r.Status == "ENABLED")
            .ToListAsync(cancellationToken);

        // 展示价算力取当前 UTC 时间
        var evaluationTime = _timeProvider.GetUtcNow().UtcDateTime;

        // 方案 A：同一票区同一时刻只展示/返回一个“当前生效档”，按售票窗口 + 优先级 + 票种序裁决
        var effectiveStrategies = strategies
            .GroupBy(p => p.SeatSectionId)
            .Select(g => PricingTierSelector.SelectEffective(g, g.Key, evaluationTime))
            .OfType<PriceStrategy>()
            .ToList();

        return effectiveStrategies.Select(p =>
        {
            decimal finalPrice = session != null
                ? PricingChange.CalculateRealtimePrice(p!.Price, session.StartTime, evaluationTime, p.SeatSectionId, dynamicRules)
                : p!.Price;

            return new PricingStrategyDto(
                p.PriceStrategyId,
                p.SeatSectionId,
                p.PriceType.ToEnum<PriceType>(),
                finalPrice,
                p.Status.ToEnum<PriceStrategyStatus>());
        });
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
            SessionStatus = SessionStatus.PRESALE.ToDbString(),
            CreateTime = DateTime.UtcNow
        };

        _context.ShowSessions.Add(sessionEntity);
        await _context.SaveChangesAsync(cancellationToken);

        return ToDto(sessionEntity);
    }

    public async Task<ShowSessionDto> UpdateSessionAsync(
        long sessionId,
        UpdateShowSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        var session = await _context.ShowSessions
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, cancellationToken);
        if (session == null)
        {
            throw new KeyNotFoundException($"未找到 ID 为 {sessionId} 的场次");
        }

        if (request.StartTime >= request.EndTime)
            throw new ArgumentException("演出结束时间必须晚于开始时间");

        if (request.SaleStartTime >= request.SaleEndTime)
            throw new ArgumentException("预售结束时间必须晚于预售开始时间");

        // 更换座位图会令既有票价策略/动态调价规则所引用的票区不再属于新的座位图，
        // 若不处理将导致用户端选座提示“区域未配置票价”。此处显式拦截，避免静默产生脏数据。
        if (session.SeatMapId != request.SeatMapId)
        {
            bool hasPricingConfig =
                await _context.PriceStrategy.AnyAsync(p => p.SessionId == sessionId, cancellationToken) ||
                await _context.DynamicPricingRules.AnyAsync(r => r.SessionId == sessionId, cancellationToken);

            if (hasPricingConfig)
                throw new InvalidOperationException(
                    "该场次已配置票价策略或动态调价规则，更换座位图会导致票区与票价不匹配；" +
                    "请先在“票价策略/动态定价”中清空（保存空列表）或重新配置后再更换座位图");
        }

        bool hasConflict = await _context.ShowSessions.CountAsync(s =>
            s.SessionId != sessionId &&
            s.SeatMapId == request.SeatMapId &&
            s.SessionStatus != SessionStatus.ENDED.ToDbString() &&
            request.StartTime < s.EndTime && request.EndTime > s.StartTime,
            cancellationToken) > 0;

        if (hasConflict)
            throw new InvalidOperationException("该场地在指定时间段内已存在其他场次排期");

        session.StartTime = request.StartTime;
        session.EndTime = request.EndTime;
        session.SaleStartTime = request.SaleStartTime;
        session.SaleEndTime = request.SaleEndTime;
        session.SeatMapId = request.SeatMapId;
        session.UpdateTime = DateTime.UtcNow;

        _context.ShowSessions.Update(session);
        await _context.SaveChangesAsync(cancellationToken);

        return ToDto(session);
    }

    public async Task ConfigurePriceStrategiesAsync(
        long sessionId,
        IEnumerable<CreatePriceStrategyRequest> requests,
        string operatorName = "admin",
        CancellationToken cancellationToken = default)
    {
        if (requests == null)
        {
            throw new ArgumentException("策略配置列表不能为 null");
        }

        var requestList = requests.ToList();

        var session = await _context.ShowSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, cancellationToken);

        if (session == null)
        {
            throw new KeyNotFoundException("演出场次不存在");
        }

        // 校验每个票档引用的票区必须属于该场次当前绑定的座位图，避免把其他座位图的票区写进来
        if (requestList.Count > 0)
        {
            var allowedSectionIds = await GetSeatMapSectionIdsAsync(session.SeatMapId, cancellationToken);
            var invalidSectionIds = requestList
                .Select(req => req.SeatSectionId)
                .Distinct()
                .Where(id => !allowedSectionIds.Contains(id))
                .OrderBy(id => id)
                .ToList();

            if (invalidSectionIds.Count > 0)
            {
                throw new ArgumentException(
                    $"票价策略中的票区 ID（{string.Join("、", invalidSectionIds)}）不属于场次 {sessionId} " +
                    $"绑定的座位图（SeatMapId={session.SeatMapId}）的票区，无法配置");
            }
        }

        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var now = DateTime.UtcNow;
            var currentOperator = string.IsNullOrWhiteSpace(operatorName) ? "admin" : operatorName;

            // 替换旧策略。ORDER_ITEM 通过外键引用已成交订单使用的票价策略行，直接删除会违反 FK；
            // 因此只删除未被订单引用的旧行，被引用的旧行改为 DISABLED 作为历史留存（不再参与生效票档选择）。
            var oldStrategies = await _context.PriceStrategy
                .Where(p => p.SessionId == sessionId)
                .ToListAsync(cancellationToken);

            if (oldStrategies.Count > 0)
            {
                var strategyIds = oldStrategies.Select(p => p.PriceStrategyId).ToList();
                var referencedIds = (await _context.Set<OrderItem>()
                        .AsNoTracking()
                        .Where(oi => strategyIds.Contains(oi.PriceStrategyId))
                        .Select(oi => oi.PriceStrategyId)
                        .Distinct()
                        .ToListAsync(cancellationToken))
                    .ToHashSet();

                var removable = oldStrategies
                    .Where(p => !referencedIds.Contains(p.PriceStrategyId))
                    .ToList();
                if (removable.Count > 0)
                {
                    _context.PriceStrategy.RemoveRange(removable);
                }

                foreach (var archived in oldStrategies.Where(p =>
                             referencedIds.Contains(p.PriceStrategyId) &&
                             p.Status != PriceStrategyStatus.DISABLED.ToDbString()))
                {
                    archived.Status = PriceStrategyStatus.DISABLED.ToDbString();
                    archived.UpdateBy = currentOperator;
                    archived.UpdateTime = now;
                }
            }

            // [] 空数组时静默清空并直接提交
            if (requestList.Count > 0)
            {
                var newStrategies = requestList.Select(req => new PriceStrategy
                {
                    SessionId = sessionId,
                    SeatSectionId = req.SeatSectionId,
                    StrategyName = string.IsNullOrWhiteSpace(req.StrategyName)
                        ? $"{req.PriceType}策略"
                        : req.StrategyName,
                    PriceType = req.PriceType.ToDbString(),
                    Price = req.Price,
                    SaleStartTime = req.SaleStartTime ?? session.SaleStartTime,
                    SaleEndTime = req.SaleEndTime ?? session.SaleEndTime,
                    Priority = req.Priority,
                    Quota = req.Quota,
                    Status = req.Status.ToDbString(),
                    CreateBy = currentOperator,
                    UpdateBy = currentOperator,
                    CreateTime = now,
                    UpdateTime = now
                }).ToList();

                _context.PriceStrategy.AddRange(newStrategies);
            }

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task ConfigureDynamicPricingRulesAsync(
        long sessionId,
        IEnumerable<CreateDynamicPricingRuleRequest> requests,
        string operatorName = "admin",
        CancellationToken cancellationToken = default)
    {
        if (requests == null)
        {
            throw new ArgumentException("动态调价规则列表不能为 null");
        }

        var requestList = requests.ToList();

        var session = await _context.ShowSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, cancellationToken);

        if (session == null)
            throw new KeyNotFoundException("演出场次不存在");

        // 校验调价时间窗口偏置 (StartOffsetMinutes 必须大于等于 EndOffsetMinutes)
        foreach (var req in requestList)
        {
            if (req.StartOffsetMinutes.HasValue && req.EndOffsetMinutes.HasValue &&
                req.StartOffsetMinutes.Value < req.EndOffsetMinutes.Value)
            {
                throw new ArgumentException($"调价时间窗口配置无效：StartOffsetMinutes ({req.StartOffsetMinutes}) 必须大于等于 EndOffsetMinutes ({req.EndOffsetMinutes})");
            }
        }

        // 指定了具体票区的规则，其票区必须属于该场次绑定的座位图；空（全局规则）允许
        if (requestList.Count > 0)
        {
            var scopedSectionIds = requestList
                .Where(req => req.SeatSectionId.HasValue)
                .Select(req => req.SeatSectionId!.Value)
                .Distinct()
                .ToList();

            if (scopedSectionIds.Count > 0)
            {
                var allowedSectionIds = await GetSeatMapSectionIdsAsync(session.SeatMapId, cancellationToken);
                var invalidSectionIds = scopedSectionIds
                    .Where(id => !allowedSectionIds.Contains(id))
                    .OrderBy(id => id)
                    .ToList();

                if (invalidSectionIds.Count > 0)
                {
                    throw new ArgumentException(
                        $"动态调价规则中的票区 ID（{string.Join("、", invalidSectionIds)}）不属于场次 {sessionId} " +
                        $"绑定的座位图（SeatMapId={session.SeatMapId}）的票区，无法配置");
                }
            }
        }

        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // 清空旧规则
            var oldRules = await _context.DynamicPricingRules
                .Where(r => r.SessionId == sessionId)
                .ToListAsync(cancellationToken);

            if (oldRules.Count > 0)
            {
                _context.DynamicPricingRules.RemoveRange(oldRules);
            }

            // [] 空数组时仅进行静默清空
            if (requestList.Count > 0)
            {
                var now = DateTime.UtcNow;
                var currentOperator = string.IsNullOrWhiteSpace(operatorName) ? "admin" : operatorName;

                var newRules = requestList.Select(req => new DynamicPricingRule
                {
                    SessionId = sessionId,
                    SeatSectionId = req.SeatSectionId,
                    RuleName = req.RuleName,
                    TriggerType = req.TriggerType,
                    StartOffsetMinutes = req.StartOffsetMinutes,
                    EndOffsetMinutes = req.EndOffsetMinutes,
                    AdjustmentType = req.AdjustmentType,
                    AdjustmentValue = req.AdjustmentValue,
                    Priority = req.Priority,
                    Status = "ENABLED",
                    CreateBy = currentOperator,
                    UpdateBy = currentOperator,
                    CreateTime = now,
                    UpdateTime = now
                }).ToList();

                _context.DynamicPricingRules.AddRange(newRules);
            }

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<HashSet<long>> GetSeatMapSectionIdsAsync(long seatMapId, CancellationToken cancellationToken)
    {
        var ids = await _context.SeatSections
            .AsNoTracking()
            .Where(s => s.SeatMapId == seatMapId)
            .Select(s => s.SeatSectionId)
            .ToListAsync(cancellationToken);

        return ids.ToHashSet();
    }

    public async Task<bool> UpdateSessionStatusAsync(
        long sessionId,
        SessionStatus newStatus,
        CancellationToken cancellationToken = default)
    {
        var session = await _context.ShowSessions.FindAsync(new object[] { sessionId }, cancellationToken);
        if (session == null)
            throw new KeyNotFoundException($"未找到 ID 为 {sessionId} 的场次");

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

    public async Task<IEnumerable<AdminPriceStrategyDto>> GetAdminPricingStrategiesAsync(
        long sessionId,
        CancellationToken cancellationToken = default)
    {
        var strategies = await _context.PriceStrategy
            .AsNoTracking()
            .Where(p => p.SessionId == sessionId)
            .OrderBy(p => p.SeatSectionId)
            .ThenBy(p => p.PriceStrategyId)
            .ToListAsync(cancellationToken);

        return strategies.Select(p => new AdminPriceStrategyDto(
            p.PriceStrategyId,
            p.SessionId,
            p.SeatSectionId,
            p.StrategyName,
            p.PriceType.ToEnum<PriceType>(),
            p.Price,
            p.SaleStartTime == default ? null : p.SaleStartTime,
            p.SaleEndTime == default ? null : p.SaleEndTime,
            p.Priority,
            p.Quota,
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
