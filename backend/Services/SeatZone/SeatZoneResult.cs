namespace ShowtimeBackend.Services.SeatZone;

public enum SeatZoneFailure
{
    None = 0,
    InvalidRequest,
    NotFound,
    Conflict
}

/// <summary>
/// 座位票区用户端服务的统一返回结果。
/// </summary>
public sealed class SeatZoneResult<T>
{
    private SeatZoneResult(T value)
    {
        IsSuccess = true;
        Value = value;
    }

    private SeatZoneResult(SeatZoneFailure failure, string errorCode, string message)
    {
        Failure = failure;
        ErrorCode = errorCode;
        Message = message;
    }

    public bool IsSuccess { get; }
    public T? Value { get; }
    public SeatZoneFailure Failure { get; }
    public string? ErrorCode { get; }
    public string? Message { get; }

    public static SeatZoneResult<T> Success(T value) => new(value);

    public static SeatZoneResult<T> Fail(
        SeatZoneFailure failure,
        string errorCode,
        string message) => new(failure, errorCode, message);
}
