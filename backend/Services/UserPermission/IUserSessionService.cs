using ShowtimeBackend.DTOs.UserPermission;

namespace ShowtimeBackend.Services.UserPermission;

public interface IUserSessionService
{
    Task<UserSessionResult<SessionIssueData>> StartAsync(
        long userId,
        ClientRequestMetadata client,
        CancellationToken cancellationToken);

    Task<UserSessionResult<SessionRefreshData>> RotateAsync(
        string refreshToken,
        CancellationToken cancellationToken);

    Task<bool> IsActiveAsync(
        long userId,
        long sessionId,
        CancellationToken cancellationToken);

    Task<int> LogoutCurrentAsync(
        long userId,
        long sessionId,
        CancellationToken cancellationToken);

    Task<int> LogoutAllAsync(
        long userId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<UserSessionResponse>> ListAsync(
        long userId,
        long currentSessionId,
        CancellationToken cancellationToken);

    Task<UserSessionResult<int>> RevokeAsync(
        long userId,
        long targetSessionId,
        CancellationToken cancellationToken);
}
