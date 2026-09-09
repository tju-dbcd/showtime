namespace ShowtimeBackend.Services.UserPermission;

public enum UserRealNameFailure
{
    None = 0,
    InvalidRequest,
    NotFound,
    Conflict,
    Internal,
}

public sealed class UserRealNameResult<T>
{
    private UserRealNameResult(T value)
    {
        IsSuccess = true;
        Value = value;
    }

    private UserRealNameResult(
        UserRealNameFailure failure,
        string errorCode,
        string message)
    {
        Failure = failure;
        ErrorCode = errorCode;
        Message = message;
    }

    public bool IsSuccess { get; }

    public T? Value { get; }

    public UserRealNameFailure Failure { get; }

    public string? ErrorCode { get; }

    public string? Message { get; }

    public static UserRealNameResult<T> Success(T value) => new(value);

    public static UserRealNameResult<T> Fail(
        UserRealNameFailure failure,
        string errorCode,
        string message) => new(failure, errorCode, message);
}
