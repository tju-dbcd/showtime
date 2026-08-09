using ShowtimeBackend.DTOs.SeatZone;

namespace ShowtimeBackend.Services.SeatZone;

public interface ISeatLockService
{
    Task<SeatZoneResult<SeatLockBatchResponse>> LockAsync(
        long userId,
        string actor,
        long sessionId,
        SeatLockBatchRequest request,
        CancellationToken cancellationToken);

    Task<SeatZoneResult<SeatLockReleaseResponse>> ReleaseAsync(
        long userId,
        string actor,
        long sessionId,
        SeatLockReleaseRequest request,
        CancellationToken cancellationToken);
}
