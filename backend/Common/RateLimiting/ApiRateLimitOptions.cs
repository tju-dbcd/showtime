namespace ShowtimeBackend.Common.RateLimiting;

public sealed class ApiRateLimitOptions
{
    public const string SectionName = "RateLimiting";

    public int LoginPerMinute { get; init; } = 5;

    public int RegisterPerMinute { get; init; } = 3;

    public int RefreshPerMinute { get; init; } = 10;

    public int AuthenticatedPerMinute { get; init; } = 120;

    public int AnonymousPerMinute { get; init; } = 60;

    public bool IsValid() =>
        LoginPerMinute > 0
        && RegisterPerMinute > 0
        && RefreshPerMinute > 0
        && AuthenticatedPerMinute > 0
        && AnonymousPerMinute > 0;
}
