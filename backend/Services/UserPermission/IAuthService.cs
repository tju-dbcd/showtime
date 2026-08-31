using ShowtimeBackend.DTOs.UserPermission;

namespace ShowtimeBackend.Services.UserPermission;

public interface IAuthService
{
    Task<AuthServiceResult<RegisterResponse>> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken);

    Task<AuthServiceResult<LoginResponse>> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken);

    /// <summary>更新当前用户头像 URL，返回更新后的用户信息（校验：http/https 绝对 URL，≤500 字符）。</summary>
    Task<AuthServiceResult<UserResponse>> UpdateAvatarAsync(
        long userId,
        string avatarUrl,
        CancellationToken cancellationToken);
}
