namespace ShowtimeBackend.Services.SeatZone;

public enum SeatZoneFailure
{
    None = 0,

    /// <summary>请求参数不符合锁座规则。</summary>
    InvalidRequest,

    /// <summary>请求的场次、座位或锁不存在。</summary>
    NotFound,

    /// <summary>场次不可售，或座位已被锁定、预留。</summary>
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
