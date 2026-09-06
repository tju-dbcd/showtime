namespace ShowtimeBackend.Services.UserPermission;

public sealed record ClientRequestMetadata(
    string? IpAddress,
    string? UserAgent);
