namespace ShowtimeBackend.DTOs.SeatZone;

public sealed record SeatMapRequest(
    long VenueId,
    string MapCode,
    string MapName,
    string MapVersion,
    bool IsDefault,
    decimal? MapWidth,
    decimal? MapHeight,
    string MapStatus,
    string? Remark);

public sealed record SeatMapResponse(
    long SeatMapId,
    long VenueId,
    string MapCode,
    string MapName,
    string MapVersion,
    bool IsDefault,
    decimal? MapWidth,
    decimal? MapHeight,
    string MapStatus,
    string? Remark);

public sealed record SeatMapListQuery(
    long? VenueId,
    string? MapStatus,
    string? Keyword,
    int Page = 1,
    int PageSize = 20);
