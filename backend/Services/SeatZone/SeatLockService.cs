using Microsoft.EntityFrameworkCore;
using Oracle.ManagedDataAccess.Client;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.SeatZone;
using ShowtimeBackend.Entities.SeatZone;

namespace ShowtimeBackend.Services.SeatZone;

/// <summary>
/// 使用 Oracle 锁座表完成临时占座和释放；数据库活动锁唯一索引负责处理并发竞争。
/// </summary>
public sealed class SeatLockService(
    AppDbContext dbContext,
    TimeProvider timeProvider,
    TimeSpan lockDuration,
    ISeatLockGuard? seatLockGuard = null,
    bool guardEnabled = true) : ISeatLockService
{
    // NUMBER(3) 的选座规则最多允许 999 个座位，同时避免超过 Oracle IN 条件数量限制。
    private const int MaxSeatsPerRequest = 999;

    // 锁座时间统一由服务端计算，防止客户端自行延长有效期；
    // 锁期（lockDuration）由调用方从配置 Redis:SeatLockTtlSeconds 注入，
    // 是 DB 锁座表 EXPIRE_TIME 与 Redis 锁 key TTL 的唯一来源，无硬编码魔法值。

    /// <summary>
    /// 批量锁定同一场次的座位；任一座位冲突时整个批次失败。
    /// </summary>
    public async Task<SeatZoneResult<SeatLockBatchResponse>> LockAsync(
        long userId,
        string actor,
        long sessionId,
        SeatLockBatchRequest request,
        CancellationToken cancellationToken)
    {
        if (sessionId <= 0 || request.SeatIds is null ||
            request.SeatIds.Count is 0 or > MaxSeatsPerRequest ||
            request.SeatIds.Any(seatId => seatId <= 0) ||
            request.SeatIds.Distinct().Count() != request.SeatIds.Count)
        {
            return SeatZoneResult<SeatLockBatchResponse>.Fail(
                SeatZoneFailure.InvalidRequest,
                "SEAT_LOCK_INVALID_REQUEST",
                "seatIds must contain valid, distinct seat identifiers.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var session = await dbContext.ShowSessions
            .AsNoTracking()
            .Where(item => item.SessionId == sessionId)
            .Select(item => new
            {
                item.SeatMapId,
                item.SessionStatus,
                item.SaleStartTime,
                item.SaleEndTime
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (session is null)
        {
            return SeatZoneResult<SeatLockBatchResponse>.Fail(
                SeatZoneFailure.NotFound,
                "SEAT_LOCK_SESSION_NOT_FOUND",
                "The requested show session does not exist.");
        }

        if (session.SessionStatus != "ONSALE" ||
            now < session.SaleStartTime || now > session.SaleEndTime)
        {
            return SeatZoneResult<SeatLockBatchResponse>.Fail(
                SeatZoneFailure.Conflict,
                "SEAT_LOCK_SESSION_UNAVAILABLE",
                "The requested show session is not currently on sale.");
        }

        var seatIds = request.SeatIds.ToArray();
        var seats = await (
                from seat in dbContext.Seats.AsNoTracking()
                join section in dbContext.SeatSections.AsNoTracking()
                    on seat.SeatSectionId equals section.SeatSectionId
                where seatIds.Contains(seat.SeatId) &&
                      section.SeatMapId == session.SeatMapId
                select new
                {
                    seat.SeatId,
                    seat.IsSellable,
                    seat.SeatStatus,
                    SectionIsSellable = section.IsSellable
                })
            .ToListAsync(cancellationToken);
        if (seats.Count != seatIds.Length)
        {
            return SeatZoneResult<SeatLockBatchResponse>.Fail(
                SeatZoneFailure.NotFound,
                "SEAT_LOCK_SEAT_NOT_FOUND",
                "One or more seats do not belong to this show session.");
        }

        if (seats.Any(item =>
                !item.IsSellable ||
                !item.SectionIsSellable ||
                item.SeatStatus != "ENABLED"))
        {
            return SeatZoneResult<SeatLockBatchResponse>.Fail(
                SeatZoneFailure.Conflict,
                "SEAT_LOCK_SEAT_UNAVAILABLE",
                "One or more seats are not sellable.");
        }

        var existingLocks = await dbContext.SeatLocks
            .Where(item => item.SessionId == sessionId &&
                           seatIds.Contains(item.SeatId) &&
                           item.LockStatus == "ACTIVE")
            .ToListAsync(cancellationToken);

        // 活动预留表示座位已经进入订单流程，不能再生成新的临时锁。
        // 使用 CountAsync 而非 AnyAsync：Oracle EF provider 会把 Any 翻译成
        // CASE WHEN EXISTS(...) THEN True ELSE False END，而 Oracle 21c 的 SQL
        // 不支持 TRUE/FALSE 布尔字面量，导致 ORA-00904: "FALSE": invalid identifier。
        var hasActiveReservation = await dbContext.SeatReservations
            .CountAsync(
                item => item.SessionId == sessionId &&
                        seatIds.Contains(item.SeatId) &&
                        item.ReservationStatus == "ACTIVE",
                cancellationToken) > 0;
        if (existingLocks.Any(item => item.ExpireTime > now) || hasActiveReservation)
        {
            return SeatZoneResult<SeatLockBatchResponse>.Fail(
                SeatZoneFailure.Conflict,
                "SEAT_LOCK_CONFLICT",
                "One or more seats are already locked or reserved.");
        }

        var expiredLocks = existingLocks
            .Where(item => item.ExpireTime <= now)
            .ToList();
        var expireTime = now.Add(lockDuration);
        var locks = seatIds.Select(seatId => new SeatLock
        {
            SessionId = sessionId,
            SeatId = seatId,
            UserId = userId,
            LockToken = Guid.NewGuid().ToString("N"),
            LockStatus = "ACTIVE",
            LockTime = now,
            ExpireTime = expireTime,
            CreateBy = actor,
            UpdateBy = actor
        }).ToList();

        // Redis 前置快速判定（在 DB 只读校验之后、写事务之前）：
        // 抢不到的请求直接 409，避免大量 INSERT 撞活动锁唯一索引的无效往返。
        // Unavailable（Redis 异常）时跳过守卫，降级为纯 Oracle 流程，购票不被阻断。
        if (seatLockGuard is not null && guardEnabled)
        {
            var acquireResult = await seatLockGuard.TryAcquireAsync(
                sessionId, locks, lockDuration, cancellationToken);
            if (acquireResult == SeatLockGuardAcquireResult.Conflict)
            {
                return SeatZoneResult<SeatLockBatchResponse>.Fail(
                    SeatZoneFailure.Conflict,
                    "SEAT_LOCK_CONFLICT",
                    "One or more seats are already locked.");
            }
        }

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            // 先把过期记录移出 ACTIVE 状态，再插入新锁，避免命中活动锁唯一索引。
            foreach (var expiredLock in expiredLocks)
            {
                expiredLock.LockStatus = "EXPIRED";
                expiredLock.ReleaseTime = now;
                expiredLock.UpdateBy = actor;
            }

            if (expiredLocks.Count > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            dbContext.SeatLocks.AddRange(locks);
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (DbUpdateException exception) when (ContainsOracleError(exception, 1))
        {
            // 两个请求同时通过前置查询/Redis 判定时，最终由 Oracle 唯一索引决定谁获得座位。
            // 仲裁失败方回滚 Redis 已获取的锁，保持双通道一致。
            await ReleaseGuardKeysAsync(sessionId, locks);
            return SeatZoneResult<SeatLockBatchResponse>.Fail(
                SeatZoneFailure.Conflict,
                "SEAT_LOCK_CONFLICT",
                "One or more seats are already locked.");
        }

        return SeatZoneResult<SeatLockBatchResponse>.Success(
            new SeatLockBatchResponse(
                sessionId,
                expireTime,
                locks.Select(item => new SeatLockItemResponse(
                    item.SeatId,
                    item.LockToken,
                    item.ExpireTime)).ToList()));
    }

    /// <summary>
    /// 批量释放当前用户持有的有效锁；令牌不完整时不释放其中任何一条。
    /// </summary>
    public async Task<SeatZoneResult<SeatLockReleaseResponse>> ReleaseAsync(
        long userId,
        string actor,
        long sessionId,
        SeatLockReleaseRequest request,
        CancellationToken cancellationToken)
    {
        if (sessionId <= 0 || request.LockTokens is null ||
            request.LockTokens.Count is 0 or > MaxSeatsPerRequest ||
            request.LockTokens.Any(string.IsNullOrWhiteSpace) ||
            request.LockTokens.Any(token => token is null || token.Length > 64) ||
            request.LockTokens.Distinct(StringComparer.Ordinal).Count() !=
            request.LockTokens.Count)
        {
            return SeatZoneResult<SeatLockReleaseResponse>.Fail(
                SeatZoneFailure.InvalidRequest,
                "SEAT_LOCK_INVALID_REQUEST",
                "lockTokens must contain valid, distinct values.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var tokens = request.LockTokens.ToArray();
        var locks = await dbContext.SeatLocks
            .Where(item => item.SessionId == sessionId &&
                           item.UserId == userId &&
                           item.LockStatus == "ACTIVE" &&
                           item.ExpireTime > now &&
                           tokens.Contains(item.LockToken))
            .ToListAsync(cancellationToken);
        if (locks.Count != tokens.Length)
        {
            return SeatZoneResult<SeatLockReleaseResponse>.Fail(
                SeatZoneFailure.NotFound,
                "SEAT_LOCK_NOT_FOUND",
                "One or more active seat locks were not found.");
        }

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        if (dbContext.Database.IsRelational())
        {
            // 条件更新会在数据库中再次检查 ACTIVE 和过期时间，防止与下单转换锁互相覆盖。
            var lockIds = locks.Select(item => item.SeatLockId).ToArray();
            var updatedCount = await dbContext.SeatLocks
                .Where(item => lockIds.Contains(item.SeatLockId) &&
                               item.LockStatus == "ACTIVE" &&
                               item.ExpireTime > now)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.LockStatus, "RELEASED")
                    .SetProperty(item => item.ReleaseTime, now)
                    .SetProperty(item => item.UpdateBy, actor),
                    cancellationToken);
            if (updatedCount != locks.Count)
            {
                await transaction!.RollbackAsync(cancellationToken);
                return SeatZoneResult<SeatLockReleaseResponse>.Fail(
                    SeatZoneFailure.NotFound,
                    "SEAT_LOCK_NOT_FOUND",
                    "One or more active seat locks were not found.");
            }
        }
        else
        {
            // InMemory 测试提供程序不支持 ExecuteUpdate，测试时使用等价的实体状态更新。
            foreach (var seatLock in locks)
            {
                seatLock.LockStatus = "RELEASED";
                seatLock.ReleaseTime = now;
                seatLock.UpdateBy = actor;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        // DB 释放成功后才释放 Redis 锁；Redis 侧失败由 TTL 兜底，不影响释放结果。
        await ReleaseGuardKeysAsync(sessionId, locks);

        return SeatZoneResult<SeatLockReleaseResponse>.Success(
            new SeatLockReleaseResponse(sessionId, locks.Count));
    }

    /// <summary>尽力释放一批座位锁的 Redis key（每把按 token 比对防误删）；无守卫时跳过。</summary>
    private async Task ReleaseGuardKeysAsync(
        long sessionId,
        IReadOnlyCollection<SeatLock> locks)
    {
        if (seatLockGuard is null)
        {
            return;
        }

        foreach (var seatLock in locks)
        {
            await seatLockGuard.ReleaseAsync(
                sessionId, seatLock.SeatId, seatLock.LockToken);
        }
    }

    private static bool ContainsOracleError(Exception exception, int number)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is OracleException oracleException && oracleException.Number == number)
            {
                return true;
            }
        }

        return false;
    }
}
