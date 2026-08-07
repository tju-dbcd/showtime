using Microsoft.EntityFrameworkCore;
using Oracle.ManagedDataAccess.Client;
using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.SeatZone;
using ShowtimeBackend.Data;
using ShowtimeBackend.Entities.SeatZone;
using ShowtimeBackend.Entities.ShowSession;

namespace ShowtimeBackend.Services.SeatZone;

public sealed record ServiceResult<T>(T? Data, int? StatusCode = null, string? Title = null, string? Detail = null)
{
    public bool IsSuccess => StatusCode is null;

    public static ServiceResult<T> Success(T data) => new(data);
    public static ServiceResult<T> Failure(int statusCode, string title, string detail) => new(default, statusCode, title, detail);
}

public sealed class SeatMapAdminService
{
    private const decimal MaxNumber10Scale2 = 99_999_999.99m;
    private static readonly HashSet<string> MapStatuses = ["DRAFT", "ENABLED", "DISABLED"];
    private static readonly HashSet<string> SectionTypes = ["NORMAL", "VIP", "ACCESSIBLE", "STANDING"];
    private readonly AppDbContext _db;

    public SeatMapAdminService(AppDbContext db) => _db = db;

    public async Task<ServiceResult<PagedResponse<SeatMapResponse>>> ListMapsAsync(SeatMapListQuery query, CancellationToken cancellationToken)
    {
        var pagingError = ValidatePaging(query.Page, query.PageSize);
        if (pagingError is not null)
            return ServiceResult<PagedResponse<SeatMapResponse>>.Failure(400, "Invalid paging", pagingError);
        if (!string.IsNullOrWhiteSpace(query.MapStatus) && !MapStatuses.Contains(query.MapStatus))
            return ServiceResult<PagedResponse<SeatMapResponse>>.Failure(400, "Invalid map status", "mapStatus must be DRAFT, ENABLED, or DISABLED.");

        var maps = _db.SeatMaps.AsNoTracking().AsQueryable();
        if (query.VenueId is not null) maps = maps.Where(map => map.VenueId == query.VenueId);
        if (!string.IsNullOrWhiteSpace(query.MapStatus)) maps = maps.Where(map => map.MapStatus == query.MapStatus);
        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.Trim();
            maps = maps.Where(map => map.MapCode.Contains(keyword) || map.MapName.Contains(keyword));
        }

        var totalCount = await maps.CountAsync(cancellationToken);
        var skip = ((long)query.Page - 1) * query.PageSize;
        var items = await maps.OrderBy(map => map.VenueId).ThenBy(map => map.MapCode).ThenBy(map => map.SeatMapId)
            .Skip((int)skip).Take(query.PageSize)
            .Select(map => ToResponse(map)).ToListAsync(cancellationToken);
        return ServiceResult<PagedResponse<SeatMapResponse>>.Success(new PagedResponse<SeatMapResponse>(items, query.Page, query.PageSize, totalCount));
    }

    public async Task<ServiceResult<SeatMapResponse>> GetMapAsync(long seatMapId, CancellationToken cancellationToken)
    {
        var map = await _db.SeatMaps.AsNoTracking().Where(item => item.SeatMapId == seatMapId)
            .Select(item => ToResponse(item)).SingleOrDefaultAsync(cancellationToken);
        return map is null
            ? ServiceResult<SeatMapResponse>.Failure(404, "Seat map not found", $"Seat map {seatMapId} does not exist.")
            : ServiceResult<SeatMapResponse>.Success(map);
    }

    public async Task<ServiceResult<SeatMapResponse>> CreateMapAsync(SeatMapRequest request, CancellationToken cancellationToken)
    {
        var validation = ValidateMap(request);
        if (validation is not null) return ServiceResult<SeatMapResponse>.Failure(400, "Invalid seat map", validation);
        if (await _db.Venues.CountAsync(venue => venue.VenueId == request.VenueId, cancellationToken) == 0)
            return ServiceResult<SeatMapResponse>.Failure(404, "Venue not found", $"Venue {request.VenueId} does not exist.");
        var mapCode = request.MapCode.Trim();
        if (await _db.SeatMaps.CountAsync(map => map.VenueId == request.VenueId && map.MapCode == mapCode, cancellationToken) > 0)
            return ServiceResult<SeatMapResponse>.Failure(409, "Duplicate seat map", "A seat map with the same venueId and mapCode already exists.");

        await using var transaction = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync(cancellationToken) : null;
        var map = new SeatMap();
        Apply(request, map);
        try
        {
            if (request.IsDefault)
            {
                var defaults = await _db.SeatMaps.Where(item => item.VenueId == request.VenueId && item.IsDefault).ToListAsync(cancellationToken);
                foreach (var item in defaults) item.IsDefault = false;
                if (defaults.Count > 0) await _db.SaveChangesAsync(cancellationToken);
            }
            _db.SeatMaps.Add(map);
            await _db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (ContainsOracleError(exception, 1))
        {
            return ServiceResult<SeatMapResponse>.Failure(409, "Unable to create seat map", "The seat map conflicts with existing data.");
        }
        return ServiceResult<SeatMapResponse>.Success(ToResponse(map));
    }

    public async Task<ServiceResult<SeatMapResponse>> UpdateMapAsync(long seatMapId, SeatMapRequest request, CancellationToken cancellationToken)
    {
        var validation = ValidateMap(request);
        if (validation is not null) return ServiceResult<SeatMapResponse>.Failure(400, "Invalid seat map", validation);
        var map = await _db.SeatMaps.SingleOrDefaultAsync(item => item.SeatMapId == seatMapId, cancellationToken);
        if (map is null) return ServiceResult<SeatMapResponse>.Failure(404, "Seat map not found", $"Seat map {seatMapId} does not exist.");
        if (await _db.Venues.CountAsync(venue => venue.VenueId == request.VenueId, cancellationToken) == 0)
            return ServiceResult<SeatMapResponse>.Failure(404, "Venue not found", $"Venue {request.VenueId} does not exist.");
        var mapCode = request.MapCode.Trim();
        if (await _db.SeatMaps.CountAsync(item => item.SeatMapId != seatMapId && item.VenueId == request.VenueId && item.MapCode == mapCode, cancellationToken) > 0)
            return ServiceResult<SeatMapResponse>.Failure(409, "Duplicate seat map", "A seat map with the same venueId and mapCode already exists.");

        await using var transaction = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            if (request.IsDefault)
            {
                var defaults = await _db.SeatMaps.Where(item => item.SeatMapId != seatMapId && item.VenueId == request.VenueId && item.IsDefault).ToListAsync(cancellationToken);
                foreach (var item in defaults) item.IsDefault = false;
                if (defaults.Count > 0) await _db.SaveChangesAsync(cancellationToken);
            }
            Apply(request, map);
            await _db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (ContainsOracleError(exception, 1))
        {
            return ServiceResult<SeatMapResponse>.Failure(409, "Unable to update seat map", "The seat map conflicts with existing data.");
        }
        return ServiceResult<SeatMapResponse>.Success(ToResponse(map));
    }

    public async Task<ServiceResult<bool>> DeleteMapAsync(long seatMapId, CancellationToken cancellationToken)
    {
        var map = await _db.SeatMaps.SingleOrDefaultAsync(item => item.SeatMapId == seatMapId, cancellationToken);
        if (map is null) return ServiceResult<bool>.Failure(404, "Seat map not found", $"Seat map {seatMapId} does not exist.");
        if (await _db.SeatSections.CountAsync(item => item.SeatMapId == seatMapId, cancellationToken) > 0 ||
            await _db.ShowSessions.CountAsync(item => item.SeatMapId == seatMapId, cancellationToken) > 0 ||
            await _db.SeatRuleScopes.CountAsync(item => item.SeatMapId == seatMapId, cancellationToken) > 0)
            return ServiceResult<bool>.Failure(409, "Seat map is in use", "Remove dependent sections, show sessions, and rule scopes before deleting this seat map.");
        _db.SeatMaps.Remove(map);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (ContainsOracleError(exception, 2292))
        {
            return ServiceResult<bool>.Failure(409, "Seat map is in use", "Remove dependent sections, show sessions, and rule scopes before deleting this seat map.");
        }
        return ServiceResult<bool>.Success(true);
    }

    public async Task<ServiceResult<PagedResponse<SeatSectionResponse>>> ListSectionsAsync(long seatMapId, SeatSectionListQuery query, CancellationToken cancellationToken)
    {
        if (await _db.SeatMaps.CountAsync(item => item.SeatMapId == seatMapId, cancellationToken) == 0)
            return ServiceResult<PagedResponse<SeatSectionResponse>>.Failure(404, "Seat map not found", $"Seat map {seatMapId} does not exist.");
        var pagingError = ValidatePaging(query.Page, query.PageSize);
        if (pagingError is not null) return ServiceResult<PagedResponse<SeatSectionResponse>>.Failure(400, "Invalid paging", pagingError);
        if (!string.IsNullOrWhiteSpace(query.SectionType) && !SectionTypes.Contains(query.SectionType))
            return ServiceResult<PagedResponse<SeatSectionResponse>>.Failure(400, "Invalid section type", "sectionType must be NORMAL, VIP, ACCESSIBLE, or STANDING.");

        var sections = _db.SeatSections.AsNoTracking().Where(item => item.SeatMapId == seatMapId);
        if (!string.IsNullOrWhiteSpace(query.SectionType)) sections = sections.Where(item => item.SectionType == query.SectionType);
        if (query.IsSellable is not null) sections = sections.Where(item => item.IsSellable == query.IsSellable);
        var totalCount = await sections.CountAsync(cancellationToken);
        var skip = ((long)query.Page - 1) * query.PageSize;
        var items = await sections.OrderBy(item => item.DisplayOrder).ThenBy(item => item.SectionCode).ThenBy(item => item.SeatSectionId)
            .Skip((int)skip).Take(query.PageSize)
            .Select(item => ToResponse(item)).ToListAsync(cancellationToken);
        return ServiceResult<PagedResponse<SeatSectionResponse>>.Success(new PagedResponse<SeatSectionResponse>(items, query.Page, query.PageSize, totalCount));
    }

    public async Task<ServiceResult<SeatSectionResponse>> GetSectionAsync(long seatSectionId, CancellationToken cancellationToken)
    {
        var section = await _db.SeatSections.AsNoTracking().Where(item => item.SeatSectionId == seatSectionId)
            .Select(item => ToResponse(item)).SingleOrDefaultAsync(cancellationToken);
        return section is null
            ? ServiceResult<SeatSectionResponse>.Failure(404, "Seat section not found", $"Seat section {seatSectionId} does not exist.")
            : ServiceResult<SeatSectionResponse>.Success(section);
    }

    public async Task<ServiceResult<SeatSectionResponse>> CreateSectionAsync(long seatMapId, SeatSectionRequest request, CancellationToken cancellationToken)
    {
        var validation = ValidateSection(request);
        if (validation is not null) return ServiceResult<SeatSectionResponse>.Failure(400, "Invalid seat section", validation);
        if (await _db.SeatMaps.CountAsync(item => item.SeatMapId == seatMapId, cancellationToken) == 0)
            return ServiceResult<SeatSectionResponse>.Failure(404, "Seat map not found", $"Seat map {seatMapId} does not exist.");
        var sectionCode = request.SectionCode.Trim();
        if (await _db.SeatSections.CountAsync(item => item.SeatMapId == seatMapId && item.SectionCode == sectionCode, cancellationToken) > 0)
            return ServiceResult<SeatSectionResponse>.Failure(409, "Duplicate seat section", "A seat section with the same seatMapId and sectionCode already exists.");
        var section = new SeatSection { SeatMapId = seatMapId };
        Apply(request, section);
        _db.SeatSections.Add(section);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (ContainsOracleError(exception, 1))
        {
            return ServiceResult<SeatSectionResponse>.Failure(409, "Unable to create seat section", "The seat section conflicts with existing data.");
        }
        return ServiceResult<SeatSectionResponse>.Success(ToResponse(section));
    }

    public async Task<ServiceResult<SeatSectionResponse>> UpdateSectionAsync(long seatSectionId, SeatSectionRequest request, CancellationToken cancellationToken)
    {
        var validation = ValidateSection(request);
        if (validation is not null) return ServiceResult<SeatSectionResponse>.Failure(400, "Invalid seat section", validation);
        var section = await _db.SeatSections.SingleOrDefaultAsync(item => item.SeatSectionId == seatSectionId, cancellationToken);
        if (section is null) return ServiceResult<SeatSectionResponse>.Failure(404, "Seat section not found", $"Seat section {seatSectionId} does not exist.");
        var sectionCode = request.SectionCode.Trim();
        if (await _db.SeatSections.CountAsync(item => item.SeatSectionId != seatSectionId && item.SeatMapId == section.SeatMapId && item.SectionCode == sectionCode, cancellationToken) > 0)
            return ServiceResult<SeatSectionResponse>.Failure(409, "Duplicate seat section", "A seat section with the same seatMapId and sectionCode already exists.");
        Apply(request, section);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (ContainsOracleError(exception, 1))
        {
            return ServiceResult<SeatSectionResponse>.Failure(409, "Unable to update seat section", "The seat section conflicts with existing data.");
        }
        return ServiceResult<SeatSectionResponse>.Success(ToResponse(section));
    }

    public async Task<ServiceResult<bool>> DeleteSectionAsync(long seatSectionId, CancellationToken cancellationToken)
    {
        var section = await _db.SeatSections.SingleOrDefaultAsync(item => item.SeatSectionId == seatSectionId, cancellationToken);
        if (section is null) return ServiceResult<bool>.Failure(404, "Seat section not found", $"Seat section {seatSectionId} does not exist.");
        if (await _db.Seats.CountAsync(item => item.SeatSectionId == seatSectionId, cancellationToken) > 0 ||
            await _db.SeatRuleScopes.CountAsync(item => item.SeatSectionId == seatSectionId, cancellationToken) > 0 ||
            await _db.Set<PriceStrategy>().CountAsync(item => item.SeatSectionId == seatSectionId, cancellationToken) > 0)
            return ServiceResult<bool>.Failure(409, "Seat section is in use", "Remove dependent seats, rule scopes, and price strategies before deleting this seat section.");
        _db.SeatSections.Remove(section);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (ContainsOracleError(exception, 2292))
        {
            return ServiceResult<bool>.Failure(409, "Seat section is in use", "Remove dependent seats, rule scopes, and price strategies before deleting this seat section.");
        }
        return ServiceResult<bool>.Success(true);
    }

    private static string? ValidateMap(SeatMapRequest request)
    {
        if (request.VenueId <= 0) return "venueId must be positive.";
        if (string.IsNullOrWhiteSpace(request.MapCode) || string.IsNullOrWhiteSpace(request.MapName)) return "mapCode and mapName are required.";
        if (request.MapCode.Length > 50 || request.MapName.Length > 100) return "mapCode must be at most 50 characters and mapName at most 100 characters.";
        if (string.IsNullOrWhiteSpace(request.MapVersion)) return "mapVersion is required.";
        if (request.MapVersion.Length > 20) return "mapVersion must be at most 20 characters.";
        if (request.Remark?.Length > 255) return "remark must be at most 255 characters.";
        if (!MapStatuses.Contains(request.MapStatus)) return "mapStatus must be DRAFT, ENABLED, or DISABLED.";
        if (!IsValidMapDimension(request.MapWidth) || !IsValidMapDimension(request.MapHeight))
            return $"mapWidth and mapHeight must be between 0.01 and {MaxNumber10Scale2} with at most two decimal places when supplied.";
        return null;
    }

    private static bool IsValidMapDimension(decimal? dimension)
    {
        if (dimension is null) return true;
        var scale = (decimal.GetBits(dimension.Value)[3] >> 16) & 0x7F;
        return dimension is >= 0.01m and <= MaxNumber10Scale2 && scale <= 2;
    }

    private static string? ValidateSection(SeatSectionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SectionCode) || string.IsNullOrWhiteSpace(request.SectionName)) return "sectionCode and sectionName are required.";
        if (request.SectionCode.Length > 30 || request.SectionName.Length > 100) return "sectionCode must be at most 30 characters and sectionName at most 100 characters.";
        if (request.SectionColor?.Length > 20 || request.FloorNo?.Length > 20) return "sectionColor and floorNo must be at most 20 characters.";
        if (request.Remark?.Length > 255) return "remark must be at most 255 characters.";
        if (!SectionTypes.Contains(request.SectionType)) return "sectionType must be NORMAL, VIP, ACCESSIBLE, or STANDING.";
        if (request.DisplayOrder is < 0 or > 99999) return "displayOrder must be between 0 and 99999.";
        return null;
    }

    private static string? ValidatePaging(int page, int pageSize)
    {
        if (page < 1 || pageSize < 1 || pageSize > 100)
            return "page must be positive and pageSize must be between 1 and 100.";
        return ((long)page - 1) * pageSize > int.MaxValue
            ? "page and pageSize produce an offset that is too large."
            : null;
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

    private static void Apply(SeatMapRequest request, SeatMap map)
    {
        map.VenueId = request.VenueId;
        map.MapCode = request.MapCode.Trim();
        map.MapName = request.MapName.Trim();
        map.MapVersion = request.MapVersion.Trim();
        map.IsDefault = request.IsDefault;
        map.MapWidth = request.MapWidth;
        map.MapHeight = request.MapHeight;
        map.MapStatus = request.MapStatus;
        map.Remark = request.Remark;
    }

    private static void Apply(SeatSectionRequest request, SeatSection section)
    {
        section.SectionCode = request.SectionCode.Trim();
        section.SectionName = request.SectionName.Trim();
        section.SectionType = request.SectionType;
        section.SectionColor = request.SectionColor;
        section.FloorNo = request.FloorNo;
        section.IsSellable = request.IsSellable;
        section.DisplayOrder = request.DisplayOrder;
        section.Remark = request.Remark;
    }

    private static SeatMapResponse ToResponse(SeatMap map) => new(map.SeatMapId, map.VenueId, map.MapCode, map.MapName, map.MapVersion, map.IsDefault, map.MapWidth, map.MapHeight, map.MapStatus, map.Remark);
    private static SeatSectionResponse ToResponse(SeatSection section) => new(section.SeatSectionId, section.SeatMapId, section.SectionCode, section.SectionName, section.SectionType, section.SectionColor, section.FloorNo, section.IsSellable, section.DisplayOrder, section.Remark);
}
