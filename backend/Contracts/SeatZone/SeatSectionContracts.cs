namespace ShowtimeBackend.Contracts.SeatZone;

public sealed record SeatSectionRequest(
    string SectionCode,
    string SectionName,
    string SectionType,
    string? SectionColor,
    string? FloorNo,
    bool IsSellable,
    int DisplayOrder,
    string? Remark);

public sealed record SeatSectionResponse(
    long SeatSectionId,
    long SeatMapId,
    string SectionCode,
    string SectionName,
    string SectionType,
    string? SectionColor,
    string? FloorNo,
    bool IsSellable,
    int DisplayOrder,
    string? Remark);

public sealed record SeatSectionListQuery(
    string? SectionType,
    bool? IsSellable,
    int Page = 1,
    int PageSize = 20);
