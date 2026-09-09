namespace ShowtimeBackend.DTOs.SeatZone;

/// <summary>
/// 场馆信息（管理端座位图创建时选择场馆用）
/// </summary>
public sealed record VenueResponse(
    long VenueId,
    string VenueName,
    string? Address,
    string? ContactPhone,
    string Status,
    string? Remark);
