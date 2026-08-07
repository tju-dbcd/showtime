namespace ShowtimeBackend.Common;

public sealed record ApiResponse<T>(
    bool Success,
    T? Data,
    string? Code,
    string Message)
{
    public static ApiResponse<T> Ok(T data, string message) =>
        new(true, data, null, message);

    public static ApiResponse<T> Fail(string code, string message) =>
        new(false, default, code, message);
}
