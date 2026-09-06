namespace ShowtimeBackend.DTOs.UserPermission;

public sealed record UserSessionResponse(
    long SessionId,
    DateTime LoginTime,
    DateTime ExpireTime,
    DateTime? LogoutTime,
    string? IpAddress,
    string? UserAgent,
    bool RiskFlag,
    string Status,
    bool IsCurrent);
