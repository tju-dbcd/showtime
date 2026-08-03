namespace ShowtimeBackend.DTOs.OrderTicket;

public sealed record MockPaymentRequest(
    string PayChannel,
    string Result);

public sealed record PaymentResponse(
    long PaymentId,
    string PaymentNo,
    long OrderId,
    decimal PayAmount,
    string PayChannel,
    string PayStatus,
    string? TradeNo,
    DateTime? CallbackTime,
    DateTime? PayTime);
