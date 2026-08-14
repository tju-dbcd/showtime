using ShowtimeBackend.Common;

namespace ShowtimeBackend.DTOs.OrderTicket;

public sealed record MockPaymentRequest(
    PaymentChannel PayChannel,
    PaymentResult Result);

/// <summary>模拟支付结果（SUCCESS/FAIL）</summary>
public enum PaymentResult
{
    SUCCESS,
    FAIL,
}

public sealed record PaymentResponse(
    long PaymentId,
    string PaymentNo,
    long OrderId,
    decimal PayAmount,
    PaymentChannel PayChannel,
    PaymentStatus PayStatus,
    string? TradeNo,
    DateTime? CallbackTime,
    DateTime? PayTime);
