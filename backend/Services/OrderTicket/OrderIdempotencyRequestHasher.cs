using System.Security.Cryptography;
using System.Text.Json;

namespace ShowtimeBackend.Services.OrderTicket;

internal static class OrderIdempotencyRequestHasher
{
    public static string Compute(
        long sessionId,
        IEnumerable<OrderIdempotencyItem> items,
        string? normalizedRemark)
    {
        var payload = new OrderIdempotencyPayload(
            sessionId,
            items.OrderBy(item => item.SeatId).ToArray(),
            normalizedRemark);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private sealed record OrderIdempotencyPayload(
        long SessionId,
        IReadOnlyList<OrderIdempotencyItem> Items,
        string? Remark);
}

internal sealed record OrderIdempotencyItem(
    long SeatId,
    long PriceStrategyId,
    long? RealNameId,
    string LockToken);
