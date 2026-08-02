namespace ShowtimeBackend.DTOs.Auth;

public sealed record LoginResponse(
    string AccessToken,
    string TokenType,
    long ExpiresIn,
    DateTime ExpiresAtUtc,
    UserResponse User);
