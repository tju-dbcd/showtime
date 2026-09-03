using ShowtimeBackend.Data;
using ShowtimeBackend.Entities.SeatZone;

namespace ShowtimeBackend.Services.SeatZone;

internal interface ISeatLockBatchWriter
{
    bool CanWrite(AppDbContext dbContext);

    Task InsertAsync(
        AppDbContext dbContext,
        IReadOnlyList<SeatLock> locks,
        CancellationToken cancellationToken);
}
