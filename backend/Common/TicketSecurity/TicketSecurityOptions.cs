namespace ShowtimeBackend.Common.TicketSecurity;

public sealed class TicketSecurityOptions
{
    public const string SectionName = "TicketSecurity";

    public string SigningKeyBase64 { get; init; } = string.Empty;
}
