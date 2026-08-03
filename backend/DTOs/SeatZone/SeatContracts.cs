namespace ShowtimeBackend.DTOs.SeatZone;

public sealed record SeatRequest(
    string RowCode,
    string SeatNo,
    int RowIndex,
    int ColIndex,
    decimal XCoord,
    decimal YCoord,
    string SeatType,
    string SeatStatus,
    bool IsAisleSide,
    bool IsSellable,
    string? Remark);

public sealed record SeatResponse(
    long SeatId,
    long SeatSectionId,
    string RowCode,
    string SeatNo,
    int RowIndex,
    int ColIndex,
    decimal XCoord,
    decimal YCoord,
    string SeatType,
    string SeatStatus,
    bool IsAisleSide,
    bool IsSellable,
    string? Remark);

public sealed record SeatListQuery(
    string? SeatType,
    string? SeatStatus,
    bool? IsSellable,
    string? RowCode,
    int Page = 1,
    int PageSize = 20);
