using ShowtimeBackend.DTOs.Auth;

namespace ShowtimeBackend.Services.Auth;

public interface IAuthService
{
    Task<AuthServiceResult<RegisterResponse>> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken);

    Task<AuthServiceResult<LoginResponse>> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken);
}
