namespace ShowtimeBackend.Services.OrderTicket;

public sealed record TicketIssuanceOutcome(
    int CreatedTicketCount,
    int ExistingTicketCount,
    int TotalTicketCount,
    DateTime IssueTime);
