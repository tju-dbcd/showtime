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
}
