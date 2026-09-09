namespace ShowtimeBackend.DTOs.UserPermission;

public sealed record RefreshTokenResponse(
    string AccessToken,
    string TokenType,
    long ExpiresIn,
    DateTime ExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc);
