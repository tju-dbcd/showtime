namespace ShowtimeBackend.Dtos.Client;

public record ShowSessionDto(
    long ShowId,
    long SessionId,
    DateTime StartTime,
    DateTime EndTime,
    DateTime SaleStartTime,
    string SessionStatus
    //decimal MinPrice
);

public record PricingStrategyDto(
    long StrategyId,
    long SeatSectionId,
    string PriceType,
    decimal Price,
    string Status
);
