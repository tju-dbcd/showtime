using ShowtimeBackend.Common;

namespace ShowtimeBackend.DTOs.SeatZone;

/// <summary>
/// 用户端选座页使用的场次座位图快照，不包含审计、锁座和订单内部数据。
/// </summary>
public sealed record SessionSeatMapDto(
    long SessionId,
    long ShowId,
    long SeatMapId,
    DateTime StartTime,
    DateTime EndTime,
    DateTime SaleStartTime,
    DateTime SaleEndTime,
    SessionStatus SessionStatus,
    SessionSeatMapMapDto SeatMap);

/// <summary>
/// 座位图及其票区层级；票区按展示顺序返回。
/// </summary>
public sealed record SessionSeatMapMapDto(
    long SeatMapId,
    long VenueId,
    string MapCode,
    string MapName,
    string MapVersion,
    bool IsDefault,
    decimal? MapWidth,
    decimal? MapHeight,
    string MapStatus,
    IReadOnlyList<SessionSeatMapSectionDto> Sections);

/// <summary>
/// 票区在用户端座位图中的展示信息。
/// </summary>
public sealed record SessionSeatMapSectionDto(
    long SeatSectionId,
    long SeatMapId,
    string SectionCode,
    string SectionName,
    string SectionType,
    string? SectionColor,
    string? FloorNo,
    bool IsSellable,
    int DisplayOrder,
    IReadOnlyList<SessionSeatMapSeatDto> Seats);

/// <summary>
/// 单个座位的静态展示与可售信息。
/// </summary>
public sealed record SessionSeatMapSeatDto(
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
    /// <summary>
    /// 当前静态可售状态：AVAILABLE 或 UNAVAILABLE。
    /// LOCKED、RESERVED 将在后续锁座模块接入后使用。
    /// </summary>
    string AvailabilityStatus);
