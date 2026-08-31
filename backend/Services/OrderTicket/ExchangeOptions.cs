using System.ComponentModel.DataAnnotations;

namespace ShowtimeBackend.Services.OrderTicket;

public sealed class ExchangeOptions
{
    public const string SectionName = "Exchange";

    [Range(1, 10_080)]
    public int ReviewExpireMinutes { get; init; } = 30;

    [Range(1, 10_080)]
    public int PaymentExpireMinutes { get; init; } = 15;

    [Range(1, 3_600)]
    public int ExpirationScanIntervalSeconds { get; init; } = 30;

    [Range(1, 500)]
    public int ExpirationBatchSize { get; init; } = 50;
}
