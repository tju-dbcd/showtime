using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.DTOs.SeatZone;
using ShowtimeBackend.Data;

namespace ShowtimeBackend.Services.SeatZone;

/// <summary>
/// 组装用户端按场次读取的座位图快照；仅计算静态可售状态，不参与锁座。
/// </summary>
public sealed class SessionSeatMapQueryService
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _timeProvider;

    public SessionSeatMapQueryService(AppDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// 以场次为入口加载座位图、票区和座位，避免向客户端暴露管理端备注及审计字段。
    /// </summary>
    public async Task<ServiceResult<SessionSeatMapDto>> GetAsync(long sessionId, CancellationToken cancellationToken)
    {
        var session = await _db.ShowSessions.AsNoTracking()
            .Where(item => item.SessionId == sessionId)
            .Select(item => new
            {
                item.SessionId,
                item.ShowId,
                item.SeatMapId,
                item.StartTime,
                item.EndTime,
                item.SaleStartTime,
                item.SaleEndTime,
                item.SessionStatus
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (session is null)
            return NotFound(sessionId);

        var map = await _db.SeatMaps.AsNoTracking()
            .Where(item => item.SeatMapId == session.SeatMapId)
            .Select(item => new
            {
                item.SeatMapId,
                item.VenueId,
                item.MapCode,
                item.MapName,
                item.MapVersion,
                item.IsDefault,
                item.MapWidth,
                item.MapHeight,
                item.MapStatus
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (map is null)
            return NotFound(sessionId);

        var sections = await _db.SeatSections.AsNoTracking()
            .Where(item => item.SeatMapId == map.SeatMapId)
            .OrderBy(item => item.DisplayOrder)
            .ThenBy(item => item.SectionCode)
            .Select(item => new
            {
                item.SeatSectionId,
                item.SeatMapId,
                item.SectionCode,
                item.SectionName,
                item.SectionType,
                item.SectionColor,
                item.FloorNo,
                item.IsSellable,
                item.DisplayOrder
            })
            .ToListAsync(cancellationToken);

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // 只把尚未过期的 ACTIVE 锁显示为 LOCKED，过期记录无需等待后台清理。
        var lockedSeatIds = await _db.SeatLocks.AsNoTracking()
            .Where(item => item.SessionId == sessionId &&
                           item.LockStatus == "ACTIVE" &&
                           item.ExpireTime > now)
            .Select(item => item.SeatId)
            .ToHashSetAsync(cancellationToken);
        var reservedSeatIds = await _db.SeatReservations.AsNoTracking()
            .Where(item => item.SessionId == sessionId &&
                           item.ReservationStatus == "ACTIVE")
            .Select(item => item.SeatId)
            .ToHashSetAsync(cancellationToken);
        var sessionIsOnSale = session.SessionStatus == "ONSALE" &&
                              session.SaleStartTime <= now &&
                              session.SaleEndTime >= now;
        var seats = await (
                from seat in _db.Seats.AsNoTracking()
                join section in _db.SeatSections.AsNoTracking() on seat.SeatSectionId equals section.SeatSectionId
                where section.SeatMapId == map.SeatMapId
                orderby seat.RowIndex, seat.ColIndex
                select new
                {
                    seat.SeatId,
                    seat.SeatSectionId,
                    seat.RowCode,
                    seat.SeatNo,
                    seat.RowIndex,
                    seat.ColIndex,
                    seat.XCoord,
                    seat.YCoord,
                    seat.SeatType,
                    seat.SeatStatus,
                    seat.IsAisleSide,
                    seat.IsSellable,
                    SectionIsSellable = section.IsSellable
                })
            .ToListAsync(cancellationToken);

        var seatsBySection = seats.GroupBy(item => item.SeatSectionId)
            .ToDictionary(item => item.Key, item => (IReadOnlyList<SessionSeatMapSeatDto>)item.Select(seat => new SessionSeatMapSeatDto(
                seat.SeatId,
                seat.SeatSectionId,
                seat.RowCode,
                seat.SeatNo,
                seat.RowIndex,
                seat.ColIndex,
                seat.XCoord,
                seat.YCoord,
                seat.SeatType,
                seat.SeatStatus,
                seat.IsAisleSide,
                seat.IsSellable,
                GetAvailabilityStatus(
                    sessionIsOnSale,
                    seat.SectionIsSellable,
                    seat.IsSellable,
                    seat.SeatStatus,
                    lockedSeatIds.Contains(seat.SeatId),
                    reservedSeatIds.Contains(seat.SeatId)))).ToList());
        var sectionDtos = sections.Select(item => new SessionSeatMapSectionDto(
            item.SeatSectionId,
            item.SeatMapId,
            item.SectionCode,
            item.SectionName,
            item.SectionType,
            item.SectionColor,
            item.FloorNo,
            item.IsSellable,
            item.DisplayOrder,
            seatsBySection.GetValueOrDefault(item.SeatSectionId, [])))
            .ToList();
        var mapDto = new SessionSeatMapMapDto(
            map.SeatMapId,
            map.VenueId,
            map.MapCode,
            map.MapName,
            map.MapVersion,
            map.IsDefault,
            map.MapWidth,
            map.MapHeight,
            map.MapStatus,
            sectionDtos);

        return ServiceResult<SessionSeatMapDto>.Success(new SessionSeatMapDto(
            session.SessionId,
            session.ShowId,
            session.SeatMapId,
            session.StartTime,
            session.EndTime,
            session.SaleStartTime,
            session.SaleEndTime,
            session.SessionStatus,
            mapDto));
    }

    private static ServiceResult<SessionSeatMapDto> NotFound(long sessionId) =>
        ServiceResult<SessionSeatMapDto>.Failure(404, "Session seat map not found", $"Session {sessionId} or its seat map does not exist.");

    private static string GetAvailabilityStatus(
        bool sessionIsOnSale,
        bool sectionIsSellable,
        bool seatIsSellable,
        string seatStatus,
        bool isLocked,
        bool isReserved)
    {
        // 静态不可售条件优先级最高，其次是正式预留，最后才是临时锁。
        if (!sessionIsOnSale || !sectionIsSellable ||
            !seatIsSellable || seatStatus != "ENABLED")
        {
            return "UNAVAILABLE";
        }

        if (isReserved)
        {
            return "RESERVED";
        }

        return isLocked ? "LOCKED" : "AVAILABLE";
    }
}
