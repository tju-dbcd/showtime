using Microsoft.Extensions.Options;
using ShowtimeBackend.Common.TicketSecurity;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Services.OrderTicket;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class TicketIssuanceServiceTests
{
    private static readonly DateTimeOffset OperationTime =
        new(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Issue_ForPendingPayment_CreatesTicketPerItemAndMarksOrderIssued()
    {
        var order = CreateOrder("PENDING_PAY", itemCount: 2, includeSuccessfulPayment: true);
        var service = CreateService();

        var result = service.Issue(
            order,
            TicketIssuanceContext.Payment,
            "alice",
            OperationTime);

        Assert.True(result.IsSuccess);
        Assert.Equal("ISSUED", order.OrderStatus);
        Assert.Equal(OperationTime.UtcDateTime, order.IssueTime);
        Assert.Equal(2, result.Value!.CreatedTicketCount);
        Assert.Equal(0, result.Value.ExistingTicketCount);
        Assert.Equal(2, result.Value.TotalTicketCount);
        Assert.All(order.Items, item =>
        {
            Assert.NotNull(item.ETicket);
            Assert.Equal(item.OrderItemId, item.ETicket.OrderItemId);
            Assert.Equal(order.UserId, item.ETicket.UserId);
            Assert.Equal("UNUSED", item.ETicket.TicketStatus);
            Assert.Equal("alice", item.ETicket.CreateBy);
            Assert.Equal("alice", item.ETicket.UpdateBy);
        });
    }

    [Fact]
    public void Issue_ForPaidCompensation_UsesCurrentOperationTime()
    {
        var order = CreateOrder("PAID", itemCount: 1, includeSuccessfulPayment: true);

        var result = CreateService().Issue(
            order,
            TicketIssuanceContext.Compensation,
            "admin",
            OperationTime);

        Assert.True(result.IsSuccess);
        Assert.Equal("ISSUED", order.OrderStatus);
        Assert.Equal(OperationTime.UtcDateTime, order.IssueTime);
    }

    [Fact]
    public void Issue_ForCompleteIssuedOrder_IsIdempotentAndPreservesTicketAndIssueTime()
    {
        var originalIssueTime = OperationTime.UtcDateTime.AddDays(-1);
        var order = CreateOrder("ISSUED", itemCount: 1, includeSuccessfulPayment: true);
        order.IssueTime = originalIssueTime;
        AttachExistingTicket(order.Items.Single(), order.UserId, "existing-qr");

        var result = CreateService().Issue(
            order,
            TicketIssuanceContext.Compensation,
            "admin",
            OperationTime);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.CreatedTicketCount);
        Assert.Equal(1, result.Value.ExistingTicketCount);
        Assert.Equal(originalIssueTime, order.IssueTime);
        Assert.Equal("existing-qr", order.Items.Single().ETicket!.QrCode);
    }

    [Fact]
    public void Issue_ForIncompleteIssuedOrder_UsesOriginalIssueTimeForRepairToken()
    {
        var originalIssueTime = OperationTime.UtcDateTime.AddDays(-1);
        var order = CreateOrder("ISSUED", itemCount: 2, includeSuccessfulPayment: true);
        order.IssueTime = originalIssueTime;
        AttachExistingTicket(order.Items.First(), order.UserId, "existing-qr");
        var tokenService = CreateTokenService();
        var service = new TicketIssuanceService(tokenService);

        var result = service.Issue(
            order,
            TicketIssuanceContext.Compensation,
            "admin",
            OperationTime);

        var repairedTicket = order.Items.OrderBy(item => item.OrderItemId).Last().ETicket!;
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.CreatedTicketCount);
        Assert.Equal(originalIssueTime, order.IssueTime);
        Assert.True(tokenService.TryValidate(repairedTicket.QrCode, out var payload));
        Assert.Equal(
            new DateTimeOffset(DateTime.SpecifyKind(originalIssueTime, DateTimeKind.Utc))
                .ToUnixTimeSeconds(),
            payload!.IssuedAtUnixSeconds);
    }

    [Theory]
    [InlineData("PENDING_PAY", TicketIssuanceContext.Compensation)]
    [InlineData("PAID", TicketIssuanceContext.Payment)]
    [InlineData("CANCELLED", TicketIssuanceContext.Compensation)]
    [InlineData("REFUNDED", TicketIssuanceContext.Compensation)]
    public void Issue_RejectsStateOutsideContextMatrix(
        string status,
        TicketIssuanceContext context)
    {
        var order = CreateOrder(status, itemCount: 1, includeSuccessfulPayment: true);

        var result = CreateService().Issue(order, context, "admin", OperationTime);

        Assert.False(result.IsSuccess);
        Assert.Equal("TICKET_ORDER_NOT_ISSUABLE", result.ErrorCode);
        Assert.Null(order.Items.Single().ETicket);
    }

    [Fact]
    public void Issue_RequiresSuccessfulPaymentInCurrentAggregate()
    {
        var order = CreateOrder("PENDING_PAY", itemCount: 1, includeSuccessfulPayment: false);

        var result = CreateService().Issue(
            order,
            TicketIssuanceContext.Payment,
            "alice",
            OperationTime);

        Assert.False(result.IsSuccess);
        Assert.Equal("TICKET_SUCCESSFUL_PAYMENT_REQUIRED", result.ErrorCode);
    }

    [Fact]
    public void Issue_RejectsOrderWithoutItems()
    {
        var order = CreateOrder("PAID", itemCount: 0, includeSuccessfulPayment: true);

        var result = CreateService().Issue(
            order,
            TicketIssuanceContext.Compensation,
            "admin",
            OperationTime);

        Assert.False(result.IsSuccess);
        Assert.Equal("TICKET_ORDER_ITEMS_EMPTY", result.ErrorCode);
    }

    [Fact]
    public void Issue_RejectsTicketCountDifferentFromItemCount()
    {
        var order = CreateOrder("PAID", itemCount: 2, includeSuccessfulPayment: true);
        order.TicketCount = 1;

        var result = CreateService().Issue(
            order,
            TicketIssuanceContext.Compensation,
            "admin",
            OperationTime);

        Assert.False(result.IsSuccess);
        Assert.Equal("TICKET_DATA_INCONSISTENT", result.ErrorCode);
    }

    [Theory]
    [InlineData("REFUNDING")]
    [InlineData("REFUNDED")]
    [InlineData("EXCHANGING")]
    [InlineData("EXCHANGED")]
    public void Issue_RejectsNonNormalOrderItem(string itemStatus)
    {
        var order = CreateOrder("PAID", itemCount: 1, includeSuccessfulPayment: true);
        order.Items.Single().ItemStatus = itemStatus;

        var result = CreateService().Issue(
            order,
            TicketIssuanceContext.Compensation,
            "admin",
            OperationTime);

        Assert.False(result.IsSuccess);
        Assert.Equal("TICKET_DATA_INCONSISTENT", result.ErrorCode);
    }

    [Theory]
    [InlineData(8, 1, "UNUSED")]
    [InlineData(7, 99, "UNUSED")]
    [InlineData(7, 1, "REFUNDED")]
    public void Issue_RejectsInconsistentExistingTicket(
        long ticketUserId,
        long ticketOrderItemId,
        string ticketStatus)
    {
        var order = CreateOrder("ISSUED", itemCount: 1, includeSuccessfulPayment: true);
        order.IssueTime = OperationTime.UtcDateTime;
        AttachExistingTicket(
            order.Items.Single(),
            ticketUserId,
            "existing-qr",
            ticketOrderItemId,
            ticketStatus);

        var result = CreateService().Issue(
            order,
            TicketIssuanceContext.Compensation,
            "admin",
            OperationTime);

        Assert.False(result.IsSuccess);
        Assert.Equal("TICKET_DATA_INCONSISTENT", result.ErrorCode);
    }

    private static TicketIssuanceService CreateService() => new(CreateTokenService());

    private static HmacTicketTokenService CreateTokenService() => new(
            Options.Create(new TicketSecurityOptions
            {
                SigningKeyBase64 =
                    "ERERERERERERERERERERERERERERERERERERERERERE=",
            }));

    private static void AttachExistingTicket(
        OrderItem item,
        long userId,
        string qrCode,
        long? orderItemId = null,
        string ticketStatus = "UNUSED")
    {
        item.ETicket = new ETicket
        {
            ETicketId = 500 + item.OrderItemId,
            ETicketNo = $"TKT-EXISTING-{item.OrderItemId}",
            OrderItemId = orderItemId ?? item.OrderItemId,
            UserId = userId,
            QrCode = qrCode,
            AntiFakeCode = $"ANTI-{item.OrderItemId}",
            TicketStatus = ticketStatus,
            OrderItem = item,
        };
    }

    private static Order CreateOrder(
        string status,
        int itemCount,
        bool includeSuccessfulPayment)
    {
        var order = new Order
        {
            OrderId = 10,
            OrderNo = "ORD000010",
            UserId = 7,
            SessionId = 20,
            TotalAmount = itemCount * 188m,
            TicketCount = itemCount,
            OrderStatus = status,
            ExpireTime = OperationTime.UtcDateTime.AddMinutes(15),
            Source = "WEB",
        };

        for (var index = 0; index < itemCount; index++)
        {
            order.Items.Add(new OrderItem
            {
                OrderItemId = index + 1,
                OrderId = order.OrderId,
                SeatId = 100 + index,
                PriceStrategyId = 200,
                UnitPrice = 188m,
                ItemStatus = "NORMAL",
                Order = order,
            });
        }

        if (includeSuccessfulPayment)
        {
            order.Payments.Add(new Payment
            {
                PaymentNo = "PAY000010",
                OrderId = order.OrderId,
                UserId = order.UserId,
                PayAmount = order.TotalAmount,
                PayChannel = "ALIPAY",
                PayStatus = "SUCCESS",
                Order = order,
            });
        }

        return order;
    }
}
