using System.ComponentModel.DataAnnotations;

namespace ShowtimeBackend.Services.OrderTicket;

public sealed class TicketRedemptionOptions
{
    public const string SectionName = "TicketRedemption";

    [Range(0, 10_080)]
    public int OpenBeforeMinutes { get; init; } = 120;

    [Range(0, 10_080)]
    public int CloseAfterMinutes { get; init; } = 120;
}
