namespace ShowtimeBackend.Services.Auth;

public sealed class AuthServiceResult<T>
{
    private AuthServiceResult(T value)
    {
        IsSuccess = true;
        Value = value;
        Failure = AuthFailure.None;
    }

    private AuthServiceResult(AuthFailure failure)
    {
        if (failure == AuthFailure.None)
        {
            throw new ArgumentOutOfRangeException(
                nameof(failure),
                "A failed result must contain a failure reason.");
        }

        IsSuccess = false;
        Failure = failure;
    }

    public bool IsSuccess { get; }

    public T? Value { get; }

    public AuthFailure Failure { get; }

    public static AuthServiceResult<T> Succeeded(T value) => new(value);

    public static AuthServiceResult<T> Failed(AuthFailure failure) => new(failure);
}
