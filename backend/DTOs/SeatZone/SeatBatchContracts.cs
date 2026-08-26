namespace ShowtimeBackend.DTOs.SeatZone;

/// <summary>
/// 管理端批量修改座位的可编辑属性。
/// SeatIds 必须属于同一个票区；未提供的属性保持原值。
/// </summary>
public sealed record SeatBatchUpdateRequest(
    IReadOnlyList<long> SeatIds,
    string? SeatType,
    string? SeatStatus,
    bool? IsAisleSide,
    bool? IsSellable);

/// <summary>
/// 批量修改成功后返回实际修改的座位。
/// </summary>
public sealed record SeatBatchUpdateResponse(
    long SeatSectionId,
    int UpdatedCount,
    IReadOnlyList<SeatResponse> Seats);
