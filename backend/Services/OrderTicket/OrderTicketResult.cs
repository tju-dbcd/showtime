namespace ShowtimeBackend.Services.OrderTicket;

public enum OrderTicketFailure
{
    None = 0,
    InvalidRequest,
    NotFound,
    Conflict
}

public sealed class OrderTicketResult<T>
{
    private OrderTicketResult(T value)
    {
        IsSuccess = true;
        Value = value;
    }

    private OrderTicketResult(OrderTicketFailure failure, string errorCode, string message)
    {
        Failure = failure;
        ErrorCode = errorCode;
        Message = message;
    }

    public bool IsSuccess { get; }
    public T? Value { get; }
    public OrderTicketFailure Failure { get; }
    public string? ErrorCode { get; }
    public string? Message { get; }

    public static OrderTicketResult<T> Success(T value) => new(value);

    public static OrderTicketResult<T> Fail(
        OrderTicketFailure failure,
        string errorCode,
        string message) => new(failure, errorCode, message);
}
