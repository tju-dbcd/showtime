using ShowtimeBackend.Entities.UserPermission;

namespace ShowtimeBackend.Services.UserPermission;

public enum UserSessionFailure
{
    None = 0,
    InvalidToken,
    Expired,
    LoggedOut,
    Locked,
    NotFound,
    UserNotFound,
    AccountUnavailable,
    TokenReused,
}

public sealed record SessionIssueData(
    long SessionId,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    bool RiskDetected);

public sealed record SessionRefreshData(
    long SessionId,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    SysUser User,
    IReadOnlyList<string> RoleCodes);

public sealed class UserSessionResult<T>
{
    private UserSessionResult(T value)
    {
        IsSuccess = true;
        Value = value;
    }

    private UserSessionResult(UserSessionFailure failure)
    {
        Failure = failure;
    }

    public bool IsSuccess { get; }

    public T? Value { get; }

    public UserSessionFailure Failure { get; }

    public static UserSessionResult<T> Succeeded(T value) => new(value);

    public static UserSessionResult<T> Failed(UserSessionFailure failure)
    {
        if (failure == UserSessionFailure.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failure));
        }

        return new UserSessionResult<T>(failure);
    }
}
