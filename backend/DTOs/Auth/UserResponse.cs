namespace ShowtimeBackend.DTOs.Auth;

public sealed record UserResponse(
    long UserId,
    string UserName,
    string? Nickname,
    string Phone,
    string? Email,
    IReadOnlyList<string> Roles);
