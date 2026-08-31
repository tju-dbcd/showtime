namespace ShowtimeBackend.DTOs.UserPermission;

public sealed record UserResponse(
    long UserId,
    string UserName,
    string? Nickname,
    string Phone,
    string? Email,
    IReadOnlyList<string> Roles,
    string? AvatarUrl);
