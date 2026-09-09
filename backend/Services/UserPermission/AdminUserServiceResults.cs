namespace ShowtimeBackend.Services.UserPermission;

public enum AdminUserFailure
{
    None = 0,
    InvalidRequest,
    NotFound,
    Conflict,
    Unavailable,
    Internal,
}

public sealed class AdminUserResult<T>
{
    private AdminUserResult(T value)
    {
        IsSuccess = true;
        Value = value;
    }

    private AdminUserResult(
        AdminUserFailure failure,
        string errorCode,
        string message)
    {
        Failure = failure;
        ErrorCode = errorCode;
        Message = message;
    }

    public bool IsSuccess { get; }

    public T? Value { get; }

    public AdminUserFailure Failure { get; }

    public string? ErrorCode { get; }

    public string? Message { get; }

    public static AdminUserResult<T> Success(T value) => new(value);

    public static AdminUserResult<T> Fail(
        AdminUserFailure failure,
        string errorCode,
        string message) => new(failure, errorCode, message);
}
