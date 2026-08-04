using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.OrderTicket;

namespace ShowtimeBackend.Services.OrderTicket;

public sealed class PaymentService(AppDbContext dbContext, TimeProvider timeProvider) : IPaymentService
{
    private static readonly HashSet<string> PaymentChannels =
    [
        "ALIPAY", "WECHAT", "UNIONPAY", "BALANCE"
    ];

    public async Task<OrderTicketResult<IReadOnlyList<PaymentResponse>>> ListAsync(
        long userId,
        long orderId,
        CancellationToken cancellationToken)
    {
        var orderExists = await dbContext.Set<Order>()
            .AsNoTracking()
            .CountAsync(item => item.OrderId == orderId && item.UserId == userId, cancellationToken) > 0;
        if (!orderExists)
        {
            return OrderTicketResult<IReadOnlyList<PaymentResponse>>.Fail(
                OrderTicketFailure.NotFound,
                "ORDER_NOT_FOUND",
                "The order does not exist.");
        }

        var payments = await dbContext.Set<Payment>()
            .AsNoTracking()
            .Where(item => item.OrderId == orderId && item.UserId == userId)
            .OrderByDescending(item => item.CreateTime)
            .ThenByDescending(item => item.PaymentId)
            .Select(item => new PaymentResponse(
                item.PaymentId,
                item.PaymentNo,
                item.OrderId,
                item.PayAmount,
                item.PayChannel,
                item.PayStatus,
                item.TradeNo,
                item.CallbackTime,
                item.PayTime))
            .ToListAsync(cancellationToken);
        return OrderTicketResult<IReadOnlyList<PaymentResponse>>.Success(payments);
    }

    public async Task<OrderTicketResult<PaymentResponse>> PayAsync(
        long userId,
        string actor,
        long orderId,
        MockPaymentRequest request,
        CancellationToken cancellationToken)
    {
        var channel = request.PayChannel?.Trim().ToUpperInvariant();
        var mockResult = request.Result?.Trim().ToUpperInvariant();
        if (channel is null || !PaymentChannels.Contains(channel) || mockResult is not ("SUCCESS" or "FAIL"))
        {
            return Invalid("PAYMENT_INVALID_REQUEST", "A valid payment channel and SUCCESS or FAIL result are required.");
        }

        var order = await dbContext.Set<Order>()
            .Include(item => item.Payments)
            .SingleOrDefaultAsync(item => item.OrderId == orderId && item.UserId == userId, cancellationToken);
        if (order is null)
        {
            return NotFound("ORDER_NOT_FOUND", "The order does not exist.");
        }

        if (order.Payments.Any(item => item.PayStatus == "SUCCESS"))
        {
            return Conflict("PAYMENT_ALREADY_SUCCEEDED", "The order already has a successful payment.");
        }

        if (order.OrderStatus != "PENDING_PAY")
        {
            return Conflict("ORDER_CANNOT_PAY", "Only pending-payment orders can be paid.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (order.ExpireTime <= now)
        {
            order.OrderStatus = "CANCELLED";
            order.CancelTime = now;
            order.UpdateBy = actor;
            await dbContext.SaveChangesAsync(cancellationToken);
            return Conflict("ORDER_EXPIRED", "The order has expired and was cancelled.");
        }

        var payment = new Payment
        {
            PaymentNo = CreateBusinessNumber("PAY", now),
            OrderId = order.OrderId,
            UserId = userId,
            PayAmount = order.TotalAmount - order.DiscountAmount,
            PayChannel = channel,
            PayStatus = mockResult,
            TradeNo = mockResult == "SUCCESS" ? CreateBusinessNumber("MOCK", now) : null,
            CallbackData = $"{{\"mockResult\":\"{mockResult}\"}}",
            CallbackTime = now,
            PayTime = mockResult == "SUCCESS" ? now : null,
            RefundAmount = 0m,
            CreateBy = actor,
            UpdateBy = actor
        };

        order.Payments.Add(payment);
        if (mockResult == "SUCCESS")
        {
            order.OrderStatus = "PAID";
            order.PayTime = now;
            order.UpdateBy = actor;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return OrderTicketResult<PaymentResponse>.Success(ToResponse(payment));
    }

    private static string CreateBusinessNumber(string prefix, DateTime now) =>
        $"{prefix}{now:yyyyMMddHHmmssfff}{Guid.NewGuid():N}"[..28].ToUpperInvariant();

    private static PaymentResponse ToResponse(Payment payment) => new(
        payment.PaymentId,
        payment.PaymentNo,
        payment.OrderId,
        payment.PayAmount,
        payment.PayChannel,
        payment.PayStatus,
        payment.TradeNo,
        payment.CallbackTime,
        payment.PayTime);

    private static OrderTicketResult<PaymentResponse> Invalid(string code, string message) =>
        OrderTicketResult<PaymentResponse>.Fail(OrderTicketFailure.InvalidRequest, code, message);

    private static OrderTicketResult<PaymentResponse> NotFound(string code, string message) =>
        OrderTicketResult<PaymentResponse>.Fail(OrderTicketFailure.NotFound, code, message);

    private static OrderTicketResult<PaymentResponse> Conflict(string code, string message) =>
        OrderTicketResult<PaymentResponse>.Fail(OrderTicketFailure.Conflict, code, message);
}
