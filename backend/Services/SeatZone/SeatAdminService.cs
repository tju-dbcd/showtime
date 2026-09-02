using Microsoft.EntityFrameworkCore;
using Oracle.ManagedDataAccess.Client;
using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.SeatZone;
using ShowtimeBackend.Data;
using ShowtimeBackend.Entities.SeatZone;

namespace ShowtimeBackend.Services.SeatZone;

public sealed class SeatAdminService
{
    private const decimal MaxNumber10Scale2 = 99_999_999.99m;
    private const int MaxBatchUpdatedSeats = 999;
    private const int PersistenceChunkSize = 100;
    // 管理端座位图一次可读取 1000 个座位；批量修改仍受独立的 999 条上限约束。
    private const int MaxSeatListPageSize = 1000;
    private static readonly HashSet<string> SeatTypes = ["NORMAL", "COUPLE", "ACCESSIBLE", "COMPANION"];
    private static readonly HashSet<string> SeatStatuses = ["ENABLED", "DISABLED", "MAINTENANCE"];
    private const string SeatHistoryConflictDetail = "Seat locks or reservations exist. Set seatStatus to DISABLED or isSellable to false instead.";
    private readonly AppDbContext _db;

    public SeatAdminService(AppDbContext db) => _db = db;

    public async Task<ServiceResult<PagedResponse<SeatResponse>>> ListSeatsAsync(long seatSectionId, SeatListQuery query, CancellationToken cancellationToken)
    {
        if (await _db.SeatSections.CountAsync(section => section.SeatSectionId == seatSectionId, cancellationToken) == 0)
            return ServiceResult<PagedResponse<SeatResponse>>.Failure(404, "Seat section not found", $"Seat section {seatSectionId} does not exist.");
        var pagingError = ValidatePaging(query.Page, query.PageSize);
        if (pagingError is not null)
            return ServiceResult<PagedResponse<SeatResponse>>.Failure(400, "Invalid paging", pagingError);
        if (!string.IsNullOrWhiteSpace(query.SeatType) && !SeatTypes.Contains(query.SeatType))
            return ServiceResult<PagedResponse<SeatResponse>>.Failure(400, "Invalid seat type", "seatType must be NORMAL, COUPLE, ACCESSIBLE, or COMPANION.");
        if (!string.IsNullOrWhiteSpace(query.SeatStatus) && !SeatStatuses.Contains(query.SeatStatus))
            return ServiceResult<PagedResponse<SeatResponse>>.Failure(400, "Invalid seat status", "seatStatus must be ENABLED, DISABLED, or MAINTENANCE.");

        var seats = _db.Seats.AsNoTracking().Where(seat => seat.SeatSectionId == seatSectionId);
        if (!string.IsNullOrWhiteSpace(query.SeatType)) seats = seats.Where(seat => seat.SeatType == query.SeatType);
        if (!string.IsNullOrWhiteSpace(query.SeatStatus)) seats = seats.Where(seat => seat.SeatStatus == query.SeatStatus);
        if (query.IsSellable is not null) seats = seats.Where(seat => seat.IsSellable == query.IsSellable);
        if (!string.IsNullOrWhiteSpace(query.RowCode)) seats = seats.Where(seat => seat.RowCode == query.RowCode.Trim());

        var totalCount = await seats.CountAsync(cancellationToken);
        var skip = ((long)query.Page - 1) * query.PageSize;
        var items = await seats.OrderBy(seat => seat.RowIndex).ThenBy(seat => seat.ColIndex).ThenBy(seat => seat.SeatId)
            .Skip((int)skip).Take(query.PageSize).Select(seat => ToResponse(seat)).ToListAsync(cancellationToken);
        return ServiceResult<PagedResponse<SeatResponse>>.Success(new PagedResponse<SeatResponse>(items, query.Page, query.PageSize, totalCount));
    }

    /// <summary>
    /// 在同一个票区内批量修改座位的可编辑属性。
    /// 完整校验通过后才跟踪修改，最后只保存一次，避免出现部分更新。
    /// </summary>
    public async Task<ServiceResult<SeatBatchUpdateResponse>> UpdateSeatsAsync(
        long seatSectionId,
        SeatBatchUpdateRequest? request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateBatchUpdateRequest(request);
        if (validation is not null)
            return ServiceResult<SeatBatchUpdateResponse>.Failure(
                400,
                "Invalid seat",
                validation);

        var seatIds = request!.SeatIds.ToArray();
        if (await _db.SeatSections.CountAsync(
                section => section.SeatSectionId == seatSectionId,
                cancellationToken) == 0)
        {
            return ServiceResult<SeatBatchUpdateResponse>.Failure(
                404,
                "Seat section not found",
                $"Seat section {seatSectionId} does not exist.");
        }

        if (!_db.Database.IsRelational())
        {
            var seats = await _db.Seats
                .Where(seat => seat.SeatSectionId == seatSectionId && seatIds.Contains(seat.SeatId))
                .OrderBy(seat => seat.RowIndex)
                .ThenBy(seat => seat.ColIndex)
                .ThenBy(seat => seat.SeatId)
                .ToListAsync(cancellationToken);

            if (seats.Count != seatIds.Length)
                return ServiceResult<SeatBatchUpdateResponse>.Failure(
                    404,
                    "Seat not found",
                    "One or more seats do not belong to the requested seat section.");

            foreach (var seat in seats)
            {
                if (request.SeatType is not null)
                    seat.SeatType = request.SeatType;
                if (request.SeatStatus is not null)
                    seat.SeatStatus = request.SeatStatus;
                if (request.IsAisleSide is not null)
                    seat.IsAisleSide = request.IsAisleSide.Value;
                if (request.IsSellable is not null)
                    seat.IsSellable = request.IsSellable.Value;
            }

            await _db.SaveChangesAsync(cancellationToken);
            return ServiceResult<SeatBatchUpdateResponse>.Success(
                new SeatBatchUpdateResponse(
                    seatSectionId,
                    seats.Count,
                    seats.Select(ToResponse).ToList()));
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var transactionCommitted = false;
        try
        {
            var seats = await _db.Seats
                .AsNoTracking()
                .Where(seat => seat.SeatSectionId == seatSectionId && seatIds.Contains(seat.SeatId))
                .OrderBy(seat => seat.RowIndex)
                .ThenBy(seat => seat.ColIndex)
                .ThenBy(seat => seat.SeatId)
                .ToListAsync(cancellationToken);

            if (seats.Count != seatIds.Length)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return ServiceResult<SeatBatchUpdateResponse>.Failure(
                    404,
                    "Seat not found",
                    "One or more seats do not belong to the requested seat section.");
            }

            var updatedCount = await _db.Seats
                .Where(seat => seat.SeatSectionId == seatSectionId && seatIds.Contains(seat.SeatId))
                .ExecuteUpdateAsync(setters =>
                {
                    if (request.SeatType is not null)
                        setters.SetProperty(seat => seat.SeatType, request.SeatType);
                    if (request.SeatStatus is not null)
                        setters.SetProperty(seat => seat.SeatStatus, request.SeatStatus);
                    if (request.IsAisleSide is not null)
                        setters.SetProperty(seat => seat.IsAisleSide, request.IsAisleSide.Value);
                    if (request.IsSellable is not null)
                        setters.SetProperty(seat => seat.IsSellable, request.IsSellable.Value);
                }, cancellationToken);

            if (updatedCount != seatIds.Length)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return ServiceResult<SeatBatchUpdateResponse>.Failure(
                    404,
                    "Seat not found",
                    "One or more seats do not belong to the requested seat section.");
            }

            await transaction.CommitAsync(cancellationToken);
            transactionCommitted = true;

            var updatedSeats = await _db.Seats
                .AsNoTracking()
                .Where(seat => seat.SeatSectionId == seatSectionId && seatIds.Contains(seat.SeatId))
                .OrderBy(seat => seat.RowIndex)
                .ThenBy(seat => seat.ColIndex)
                .ThenBy(seat => seat.SeatId)
                .ToListAsync(cancellationToken);

            return ServiceResult<SeatBatchUpdateResponse>.Success(
                new SeatBatchUpdateResponse(
                    seatSectionId,
                    updatedSeats.Count,
                    updatedSeats.Select(ToResponse).ToList()));
        }
        catch
        {
            if (!transactionCommitted)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<ServiceResult<SeatResponse>> GetSeatAsync(long seatId, CancellationToken cancellationToken)
    {
        var seat = await _db.Seats.AsNoTracking().Where(item => item.SeatId == seatId).Select(item => ToResponse(item)).SingleOrDefaultAsync(cancellationToken);
        return seat is null
            ? ServiceResult<SeatResponse>.Failure(404, "Seat not found", $"Seat {seatId} does not exist.")
            : ServiceResult<SeatResponse>.Success(seat);
    }

    public async Task<ServiceResult<SeatResponse>> CreateSeatAsync(long seatSectionId, SeatRequest request, CancellationToken cancellationToken)
    {
        var validation = Validate(request);
        if (validation is not null) return ServiceResult<SeatResponse>.Failure(400, "Invalid seat", validation);
        if (await _db.SeatSections.CountAsync(section => section.SeatSectionId == seatSectionId, cancellationToken) == 0)
            return ServiceResult<SeatResponse>.Failure(404, "Seat section not found", $"Seat section {seatSectionId} does not exist.");
        var uniquenessError = await FindUniquenessConflictAsync(seatSectionId, request, null, cancellationToken);
        if (uniquenessError is not null) return ServiceResult<SeatResponse>.Failure(409, "Duplicate seat", uniquenessError);

        var seat = new Seat { SeatSectionId = seatSectionId };
        Apply(request, seat);
        _db.Seats.Add(seat);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (ContainsOracleError(exception, 1))
        {
            return ServiceResult<SeatResponse>.Failure(409, "Unable to create seat", "The seat conflicts with existing data.");
        }
        return ServiceResult<SeatResponse>.Success(ToResponse(seat));
    }

    public async Task<ServiceResult<SeatResponse>> UpdateSeatAsync(long seatId, SeatRequest request, CancellationToken cancellationToken)
    {
        var validation = Validate(request);
        if (validation is not null) return ServiceResult<SeatResponse>.Failure(400, "Invalid seat", validation);
        var seat = await _db.Seats.SingleOrDefaultAsync(item => item.SeatId == seatId, cancellationToken);
        if (seat is null) return ServiceResult<SeatResponse>.Failure(404, "Seat not found", $"Seat {seatId} does not exist.");
        var uniquenessError = await FindUniquenessConflictAsync(seat.SeatSectionId, request, seatId, cancellationToken);
        if (uniquenessError is not null) return ServiceResult<SeatResponse>.Failure(409, "Duplicate seat", uniquenessError);

        Apply(request, seat);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (ContainsOracleError(exception, 1))
        {
            return ServiceResult<SeatResponse>.Failure(409, "Unable to update seat", "The seat conflicts with existing data.");
        }
        return ServiceResult<SeatResponse>.Success(ToResponse(seat));
    }

    public async Task<ServiceResult<bool>> DeleteSeatAsync(long seatId, CancellationToken cancellationToken)
    {
        var seat = await _db.Seats.SingleOrDefaultAsync(item => item.SeatId == seatId, cancellationToken);
        if (seat is null) return ServiceResult<bool>.Failure(404, "Seat not found", $"Seat {seatId} does not exist.");
        if (await _db.SeatLocks.CountAsync(item => item.SeatId == seatId, cancellationToken) > 0 ||
            await _db.SeatReservations.CountAsync(item => item.SeatId == seatId, cancellationToken) > 0)
            return ServiceResult<bool>.Failure(409, "Seat has history", SeatHistoryConflictDetail);
        _db.Seats.Remove(seat);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (ContainsOracleError(exception, 2292))
        {
            return ServiceResult<bool>.Failure(409, "Seat has history", SeatHistoryConflictDetail);
        }
        return ServiceResult<bool>.Success(true);
    }

    private async Task<string?> FindUniquenessConflictAsync(long seatSectionId, SeatRequest request, long? excludedSeatId, CancellationToken cancellationToken)
    {
        var rowCode = request.RowCode.Trim();
        var seatNo = request.SeatNo.Trim();
        if (await _db.Seats.CountAsync(seat => seat.SeatSectionId == seatSectionId && seat.RowCode == rowCode && seat.SeatNo == seatNo && seat.SeatId != excludedSeatId, cancellationToken) > 0)
            return "A seat with the same seatSectionId, rowCode, and seatNo already exists.";
        if (await _db.Seats.CountAsync(seat => seat.SeatSectionId == seatSectionId && seat.RowIndex == request.RowIndex && seat.ColIndex == request.ColIndex && seat.SeatId != excludedSeatId, cancellationToken) > 0)
            return "A seat with the same seatSectionId, rowIndex, and colIndex already exists.";
        return null;
    }

    private static string? Validate(SeatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RowCode) || string.IsNullOrWhiteSpace(request.SeatNo)) return "rowCode and seatNo are required.";
        if (request.RowCode.Length > 20 || request.SeatNo.Length > 20) return "rowCode and seatNo must be at most 20 characters.";
        if (request.Remark?.Length > 255) return "remark must be at most 255 characters.";
        if (!SeatTypes.Contains(request.SeatType)) return "seatType must be NORMAL, COUPLE, ACCESSIBLE, or COMPANION.";
        if (!SeatStatuses.Contains(request.SeatStatus)) return "seatStatus must be ENABLED, DISABLED, or MAINTENANCE.";
        if (request.RowIndex is < 0 or > 99999 || request.ColIndex is < 0 or > 99999) return "rowIndex and colIndex must be between 0 and 99999.";
        if (request.XCoord is < -MaxNumber10Scale2 or > MaxNumber10Scale2 || request.YCoord is < -MaxNumber10Scale2 or > MaxNumber10Scale2)
            return $"xCoord and yCoord must be between {-MaxNumber10Scale2} and {MaxNumber10Scale2}.";
        return null;
    }

    private static string? ValidatePaging(int page, int pageSize)
    {
        if (page < 1 || pageSize < 1 || pageSize > MaxSeatListPageSize)
            return $"page must be positive and pageSize must be between 1 and {MaxSeatListPageSize}.";
        return ((long)page - 1) * pageSize > int.MaxValue
            ? "page and pageSize produce an offset that is too large."
            : null;
    }

    private static string? ValidateBatchUpdateRequest(SeatBatchUpdateRequest? request)
    {
        if (request is null)
            return "request is required.";
        if (request.SeatIds is null ||
            request.SeatIds.Count is 0 or > MaxBatchUpdatedSeats ||
            request.SeatIds.Any(seatId => seatId <= 0))
            return $"seatIds must contain between 1 and {MaxBatchUpdatedSeats} positive values.";
        if (request.SeatIds.Distinct().Count() != request.SeatIds.Count)
            return "seatIds must not contain duplicates.";
        if (request.SeatType is null &&
            request.SeatStatus is null &&
            request.IsAisleSide is null &&
            request.IsSellable is null)
            return "at least one editable seat property is required.";
        if (request.SeatType is not null && !SeatTypes.Contains(request.SeatType))
            return "seatType must be NORMAL, COUPLE, ACCESSIBLE, or COMPANION.";
        if (request.SeatStatus is not null && !SeatStatuses.Contains(request.SeatStatus))
            return "seatStatus must be ENABLED, DISABLED, or MAINTENANCE.";
        return null;
    }

    private static bool ContainsOracleError(Exception exception, int number)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is OracleException oracleException && oracleException.Number == number)
                return true;
        }
        return false;
    }

    private static void Apply(SeatRequest request, Seat seat)
    {
        seat.RowCode = request.RowCode.Trim();
        seat.SeatNo = request.SeatNo.Trim();
        seat.RowIndex = request.RowIndex;
        seat.ColIndex = request.ColIndex;
        seat.XCoord = request.XCoord;
        seat.YCoord = request.YCoord;
        seat.SeatType = request.SeatType;
        seat.SeatStatus = request.SeatStatus;
        seat.IsAisleSide = request.IsAisleSide;
        seat.IsSellable = request.IsSellable;
        seat.Remark = request.Remark;
    }

    private static SeatResponse ToResponse(Seat seat) => new(seat.SeatId, seat.SeatSectionId, seat.RowCode, seat.SeatNo, seat.RowIndex, seat.ColIndex, seat.XCoord, seat.YCoord, seat.SeatType, seat.SeatStatus, seat.IsAisleSide, seat.IsSellable, seat.Remark);
}
