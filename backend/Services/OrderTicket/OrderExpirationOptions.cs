using System.ComponentModel.DataAnnotations;

namespace ShowtimeBackend.Services.OrderTicket;

public sealed class OrderExpirationOptions
{
    public const string SectionName = "OrderExpiration";

    [Range(1, 1_440)]
    public int PendingPaymentExpireMinutes { get; init; } = 15;

    [Range(1, 3_600)]
    public int ExpirationScanIntervalSeconds { get; init; } = 30;

    [Range(1, 500)]
    public int ExpirationBatchSize { get; init; } = 50;
}
