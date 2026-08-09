using Microsoft.EntityFrameworkCore;
using Oracle.ManagedDataAccess.Client;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.SeatZone;
using ShowtimeBackend.Entities.SeatZone;

namespace ShowtimeBackend.Services.SeatZone;

public sealed class SeatLockService(
    AppDbContext dbContext,
    TimeProvider timeProvider) : ISeatLockService
{
    private const int MaxSeatsPerRequest = 999;
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(10);

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
        var hasActiveReservation = await dbContext.SeatReservations
            .AnyAsync(
                item => item.SessionId == sessionId &&
                        seatIds.Contains(item.SeatId) &&
                        item.ReservationStatus == "ACTIVE",
                cancellationToken);
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
        var expireTime = now.Add(LockDuration);
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

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
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

        return SeatZoneResult<SeatLockReleaseResponse>.Success(
            new SeatLockReleaseResponse(sessionId, locks.Count));
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
