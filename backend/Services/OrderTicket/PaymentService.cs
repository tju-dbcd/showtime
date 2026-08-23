using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Common;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.OrderTicket;

namespace ShowtimeBackend.Services.OrderTicket;

public sealed class PaymentService(
    AppDbContext dbContext,
    TimeProvider timeProvider,
    ITicketIssuanceService ticketIssuanceService,
    ILogger<PaymentService> logger) : IPaymentService
{
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

        // 先物化实体再映射 DTO：字符串状态转枚举在内存中完成（EF 无法在 SQL 中转换）
        var payments = await dbContext.Set<Payment>()
            .AsNoTracking()
            .Where(item => item.OrderId == orderId && item.UserId == userId)
            .OrderByDescending(item => item.CreateTime)
            .ThenByDescending(item => item.PaymentId)
            .ToListAsync(cancellationToken);

        var items = payments.Select(ToResponse).ToList();
        return OrderTicketResult<IReadOnlyList<PaymentResponse>>.Success(items);
    }

    public async Task<OrderTicketResult<PaymentProcessResponse>> PayAsync(
        long userId,
        string actor,
        long orderId,
        MockPaymentRequest request,
        CancellationToken cancellationToken)
    {
        // PayChannel / Result 已由 DTO 枚举 + JSON 模型绑定保证合法（取值与 CHK_PAYMENT_CHANNEL 一致）
        var channel = request.PayChannel.ToDbString();
        var mockResult = request.Result.ToDbString();

        var order = await dbContext.Set<Order>()
            .Include(item => item.Payments)
            .Include(item => item.Items)
                .ThenInclude(item => item.ETicket)
            .SingleOrDefaultAsync(item => item.OrderId == orderId && item.UserId == userId, cancellationToken);
        if (order is null)
        {
            return NotFound("ORDER_NOT_FOUND", "The order does not exist.");
        }

        if (order.Payments.Any(item => item.PayStatus == PaymentStatus.SUCCESS.ToDbString()))
        {
            return Conflict("PAYMENT_ALREADY_SUCCEEDED", "The order already has a successful payment.");
        }

        if (order.OrderStatus != OrderStatus.PENDING_PAY.ToDbString())
        {
            return Conflict("ORDER_CANNOT_PAY", "Only pending-payment orders can be paid.");
        }

        var operationTime = timeProvider.GetUtcNow();
        var now = operationTime.UtcDateTime;
        if (order.ExpireTime <= now)
        {
            order.OrderStatus = OrderStatus.CANCELLED.ToDbString();
            order.CancelTime = now;
            order.UpdateBy = actor;
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Conflict(
                    "ORDER_CANNOT_PAY",
                    "The order status changed and it can no longer be paid.");
            }
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
            TradeNo = request.Result == PaymentResult.SUCCESS ? CreateBusinessNumber("MOCK", now) : null,
            CallbackData = $"{{\"mockResult\":\"{mockResult}\"}}",
            CallbackTime = now,
            PayTime = request.Result == PaymentResult.SUCCESS ? now : null,
            RefundAmount = 0m,
            CreateBy = actor,
            UpdateBy = actor
        };

        order.Payments.Add(payment);
        TicketIssuanceOutcome? issuance = null;
        if (request.Result == PaymentResult.SUCCESS)
        {
            order.PayTime = now;
            order.UpdateBy = actor;
            OrderTicketResult<TicketIssuanceOutcome> issuanceResult;
            try
            {
                issuanceResult = ticketIssuanceService.Issue(
                    order,
                    TicketIssuanceContext.Payment,
                    actor,
                    operationTime);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Ticket issuance failed before saving payment for order {OrderId}.",
                    order.OrderId);
                dbContext.ChangeTracker.Clear();
                return OrderTicketResult<PaymentProcessResponse>.Fail(
                    OrderTicketFailure.Internal,
                    "TICKET_ISSUANCE_FAILED",
                    "Ticket issuance failed.");
            }
            if (!issuanceResult.IsSuccess)
            {
                dbContext.ChangeTracker.Clear();
                return OrderTicketResult<PaymentProcessResponse>.Fail(
                    issuanceResult.Failure,
                    issuanceResult.ErrorCode!,
                    issuanceResult.Message!);
            }

            issuance = issuanceResult.Value;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(
                "ORDER_CANNOT_PAY",
                "The order status changed and it can no longer be paid.");
        }
        return OrderTicketResult<PaymentProcessResponse>.Success(
            new PaymentProcessResponse(
                ToResponse(payment),
                order.OrderStatus.ToEnum<OrderStatus>(),
                issuance?.TotalTicketCount ??
                    order.Items.Count(item => item.ETicket is not null)));
    }

    private static string CreateBusinessNumber(string prefix, DateTime now) =>
        $"{prefix}{now:yyyyMMddHHmmssfff}{Guid.NewGuid():N}"[..28].ToUpperInvariant();

    private static PaymentResponse ToResponse(Payment payment) => new(
        payment.PaymentId,
        payment.PaymentNo,
        payment.OrderId,
        payment.PayAmount,
        payment.PayChannel.ToEnum<PaymentChannel>(),
        payment.PayStatus.ToEnum<PaymentStatus>(),
        payment.TradeNo,
        payment.CallbackTime,
        payment.PayTime);

    private static OrderTicketResult<PaymentProcessResponse> Invalid(string code, string message) =>
        OrderTicketResult<PaymentProcessResponse>.Fail(OrderTicketFailure.InvalidRequest, code, message);

    private static OrderTicketResult<PaymentProcessResponse> NotFound(string code, string message) =>
        OrderTicketResult<PaymentProcessResponse>.Fail(OrderTicketFailure.NotFound, code, message);

    private static OrderTicketResult<PaymentProcessResponse> Conflict(string code, string message) =>
        OrderTicketResult<PaymentProcessResponse>.Fail(OrderTicketFailure.Conflict, code, message);
}
