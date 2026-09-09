using ShowtimeBackend.Entities.SeatZone;
using ShowtimeBackend.Services.SeatZone;

namespace ShowtimeBackend.Tests.SeatZone;

public sealed class OracleSeatLockBatchWriterTests
{
    public static TheoryData<int, int[]> BatchCases => new()
    {
        { 1, [1] },
        { 100, [100] },
        { 500, [500] },
        { 501, [500, 1] },
        { 999, [500, 499] }
    };

    [Theory]
    [MemberData(nameof(BatchCases))]
    public void CreateBatches_UsesFixedFiveHundredSeatChunks(
        int count,
        int[] expectedBatchSizes)
    {
        var locks = Enumerable.Range(1, count)
            .Select(index => new SeatLock { SeatId = index })
            .ToArray();

        var actualBatches = OracleSeatLockBatchWriter.CreateBatches(locks)
            .ToArray();

        var actual = actualBatches
            .Select(batch => batch.Length)
            .ToArray();

        Assert.Equal(expectedBatchSizes, actual);

        var actualSeatIds = actualBatches
            .SelectMany(batch => batch)
            .Select(seatLock => seatLock.SeatId)
            .ToArray();

        Assert.Equal(Enumerable.Range(1, count).Select(static seatId => (long)seatId), actualSeatIds);
    }
}
