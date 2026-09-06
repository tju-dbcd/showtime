namespace ShowtimeBackend.DTOs.UserPermission;

public sealed record LoginResponse(
    string AccessToken,
    string TokenType,
    long ExpiresIn,
    DateTime ExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    UserResponse User);
