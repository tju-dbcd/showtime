using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Entities.SeatZone;
using ShowtimeBackend.Entities.ShowSession;
using ShowtimeBackend.Services.OrderTicket;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class RefundApplicationServiceTests
{
    [Fact]
    public async Task QuoteAsync_UsesUniqueSuccessfulPaymentAsNetPaid()
    {
        await using var fixture = await RefundTestData.CreateIssuedOrderAsync(
            totalAmount: 300m,
            discountAmount: 60m,
            payAmount: 240m,
            itemPrices: [100m, 200m]);
        fixture.Db.Add(Policy(refundRate: 0.8m, serviceFee: 5m));
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.CreateApplicationService().QuoteAsync(
            fixture.UserId,
            fixture.OrderId,
            new RefundQuoteRequest([fixture.OrderItemIds[0]]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(80m, result.Value!.RefundAmount);
        Assert.Equal(59m, result.Value.ActualRefund);
        Assert.Equal(RefundType.PART, result.Value.RefundType);
        Assert.Equal(RefundTestData.FixedUtcNow, result.Value.QuotedAt);
        Assert.Equal(1, fixture.TimeProvider.GetUtcNowCallCount);
    }

    [Theory]
    [InlineData(PaymentFault.NoSuccessfulPayment)]
    [InlineData(PaymentFault.TwoSuccessfulPayments)]
    [InlineData(PaymentFault.NonPositivePayAmount)]
    [InlineData(PaymentFault.PaymentDoesNotMatchOrderNet)]
    [InlineData(PaymentFault.ItemSumDoesNotMatchTotal)]
    [InlineData(PaymentFault.ZeroDenominator)]
    public async Task QuoteAsync_RejectsInconsistentPaymentData(PaymentFault fault)
    {
        await using var fixture = await CreatePaymentFaultFixtureAsync(fault);
        fixture.Db.Add(Policy());
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.CreateApplicationService().QuoteAsync(
            fixture.UserId,
            fixture.OrderId,
            new RefundQuoteRequest([fixture.OrderItemIds[0]]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderTicketFailure.Conflict, result.Failure);
        Assert.Equal("REFUND_PAYMENT_DATA_INCONSISTENT", result.ErrorCode);
    }

    [Fact]
    public async Task QuoteAsync_RejectsNegativeSuccessfulPaymentAmount()
    {
        await using var fixture = await RefundTestData.CreateIssuedOrderAsync();
        fixture.Db.Add(Policy());
        await fixture.Db.SaveChangesAsync();
        await fixture.Db.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints = ON;");
        await fixture.Db.Database.ExecuteSqlRawAsync(
            "UPDATE PAYMENT SET PAY_AMOUNT = -1 WHERE PAYMENT_ID = 31;");
        await fixture.Db.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints = OFF;");
        fixture.Db.ChangeTracker.Clear();

        var result = await QuoteFirstItemAsync(fixture);

        AssertConflict(result, "REFUND_PAYMENT_DATA_INCONSISTENT");
    }

    [Fact]
    public async Task QuoteAsync_HidesOrderOwnedByAnotherUser()
    {
        await using var fixture = await RefundTestData.CreateIssuedOrderAsync();

        var result = await fixture.CreateApplicationService().QuoteAsync(
            fixture.UserId + 1,
            fixture.OrderId,
            new RefundQuoteRequest([fixture.OrderItemIds[0]]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderTicketFailure.NotFound, result.Failure);
        Assert.Equal("REFUND_ORDER_NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public async Task QuoteAsync_RejectsEmptyItemList()
    {
        await using var fixture = await RefundTestData.CreateIssuedOrderAsync();

        var result = await fixture.CreateApplicationService().QuoteAsync(
            fixture.UserId,
            fixture.OrderId,
            new RefundQuoteRequest([]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderTicketFailure.InvalidRequest, result.Failure);
        Assert.Equal("REFUND_ITEM_IDS_REQUIRED", result.ErrorCode);
    }

    [Fact]
    public async Task QuoteAsync_RejectsDuplicateItemIds()
    {
        await using var fixture = await RefundTestData.CreateIssuedOrderAsync();
        var itemId = fixture.OrderItemIds[0];

        var result = await fixture.CreateApplicationService().QuoteAsync(
            fixture.UserId,
            fixture.OrderId,
            new RefundQuoteRequest([itemId, itemId]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderTicketFailure.InvalidRequest, result.Failure);
        Assert.Equal("REFUND_ITEM_IDS_DUPLICATED", result.ErrorCode);
    }

    [Fact]
    public async Task QuoteAsync_RejectsItemOutsideOrderAsNotEligible()
    {
        await using var fixture = await RefundTestData.CreateIssuedOrderAsync();

        var result = await fixture.CreateApplicationService().QuoteAsync(
            fixture.UserId,
            fixture.OrderId,
            new RefundQuoteRequest([999]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderTicketFailure.Conflict, result.Failure);
        Assert.Equal("REFUND_ITEM_NOT_ELIGIBLE", result.ErrorCode);
    }

    [Theory]
    [InlineData("PENDING_PAY")]
    [InlineData("PAID")]
    [InlineData("REFUNDED")]
    [InlineData("CANCELLED")]
    public async Task QuoteAsync_RejectsIneligibleOrderStatus(string orderStatus)
    {
        await using var fixture = await RefundTestData.CreateIssuedOrderAsync();
        var order = await fixture.Db.Set<Order>().SingleAsync();
        order.OrderStatus = orderStatus;
        await fixture.Db.SaveChangesAsync();

        var result = await QuoteFirstItemAsync(fixture);

        AssertConflict(result, "REFUND_ORDER_NOT_ELIGIBLE");
    }

    [Fact]
    public async Task QuoteAsync_AllowsPartRefundOrderStatus()
    {
        await using var fixture = await RefundTestData.CreateIssuedOrderAsync();
        var order = await fixture.Db.Set<Order>().SingleAsync();
        order.OrderStatus = "PART_REFUND";
        fixture.Db.Add(Policy());
        await fixture.Db.SaveChangesAsync();

        var result = await QuoteFirstItemAsync(fixture);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task QuoteAsync_RejectsSessionThatHasStarted()
    {
        await using var fixture = await RefundTestData.CreateIssuedOrderAsync();
        var session = await fixture.Db.Set<ShowSession>().SingleAsync();
        session.StartTime = RefundTestData.FixedUtcNow;
        await fixture.Db.SaveChangesAsync();

        var result = await QuoteFirstItemAsync(fixture);

        AssertConflict(result, "REFUND_SESSION_STARTED");
    }

    [Fact]
    public async Task QuoteAsync_RejectsNonNormalItem()
    {
        await using var fixture = await RefundTestData.CreateIssuedOrderAsync();
        var item = await fixture.Db.Set<OrderItem>()
            .SingleAsync(x => x.OrderItemId == fixture.OrderItemIds[0]);
        item.ItemStatus = "REFUNDING";
        await fixture.Db.SaveChangesAsync();

        var result = await QuoteFirstItemAsync(fixture);

        AssertConflict(result, "REFUND_ITEM_NOT_ELIGIBLE");
    }

    [Fact]
    public async Task QuoteAsync_RejectsNonUnusedTicket()
    {
        await using var fixture = await RefundTestData.CreateIssuedOrderAsync();
        var ticket = await fixture.Db.Set<ETicket>()
            .SingleAsync(x => x.OrderItemId == fixture.OrderItemIds[0]);
        ticket.TicketStatus = "USED";
        await fixture.Db.SaveChangesAsync();

        var result = await QuoteFirstItemAsync(fixture);

        AssertConflict(result, "REFUND_TICKET_NOT_UNUSED");
    }

    [Fact]
    public async Task QuoteAsync_RejectsMissingTicket()
    {
        await using var fixture = await RefundTestData.CreateIssuedOrderAsync();
        var ticket = await fixture.Db.Set<ETicket>()
            .SingleAsync(x => x.OrderItemId == fixture.OrderItemIds[0]);
        fixture.Db.Remove(ticket);
        await fixture.Db.SaveChangesAsync();

        var result = await QuoteFirstItemAsync(fixture);

        AssertConflict(result, "REFUND_TICKET_NOT_UNUSED");
    }

    [Theory]
    [InlineData("SYSTEM", "ACTIVE")]
    [InlineData("ORDER", "RELEASED")]
    public async Task QuoteAsync_RequiresExactlyOneActiveOrderReservation(
        string reservationType,
        string reservationStatus)
    {
        await using var fixture = await RefundTestData.CreateIssuedOrderAsync();
        var reservation = await fixture.Db.Set<SeatReservation>()
            .SingleAsync(x => x.OrderItemId == fixture.OrderItemIds[0]);
        reservation.ReservationType = reservationType;
        reservation.ReservationStatus = reservationStatus;
        if (reservationType != "ORDER")
        {
            reservation.OrderItemId = null;
        }
        await fixture.Db.SaveChangesAsync();

        var result = await QuoteFirstItemAsync(fixture);

        AssertConflict(result, "REFUND_RESERVATION_DATA_INCONSISTENT");
    }

    [Fact]
    public async Task QuoteAsync_RejectsDuplicateActiveOrderReservationsAsInconsistentData()
    {
        await using var fixture = await RefundTestData.CreateIssuedOrderAsync();
        await fixture.Db.Database.ExecuteSqlRawAsync(
            "DROP INDEX UK_SEAT_RESERVATION_ORDER_ITEM;");
        fixture.Db.Add(new SeatReservation
        {
            SeatReservationId = 399,
            SessionId = 21,
            SeatId = 501,
            OrderItemId = fixture.OrderItemIds[0],
            ReservationType = "ORDER",
            ReservationStatus = "ACTIVE",
            ReserveTime = RefundTestData.FixedUtcNow.AddHours(-2),
        });
        await fixture.Db.SaveChangesAsync();

        var result = await QuoteFirstItemAsync(fixture);

        AssertConflict(result, "REFUND_RESERVATION_DATA_INCONSISTENT");
    }

    [Fact]
    public async Task QuoteAsync_RejectsItemAlreadyRelatedToRefund()
    {
        await using var fixture = await RefundTestData.CreateIssuedOrderAsync();
        fixture.Db.Add(new RefundItem
        {
            RefundItemId = 401,
            RefundId = 501,
            OrderItemId = fixture.OrderItemIds[0],
            RefundBaseAmount = 105m,
        });
        await fixture.Db.SaveChangesAsync();

        var result = await QuoteFirstItemAsync(fixture);

        AssertConflict(result, "REFUND_ITEM_ALREADY_REQUESTED");
    }

    [Fact]
    public async Task QuoteAsync_RejectsItemAlreadyRelatedToExchange()
    {
        await using var fixture = await RefundTestData.CreateIssuedOrderAsync();
        fixture.Db.Add(new ExchangeItem
        {
            ExchangeItemId = 601,
            ExchangeId = 701,
            OrderItemId = fixture.OrderItemIds[0],
            NewOrderItemId = 999,
        });
        await fixture.Db.SaveChangesAsync();

        var result = await QuoteFirstItemAsync(fixture);

        AssertConflict(result, "REFUND_ITEM_EXCHANGE_CONFLICT");
    }

    [Fact]
    public async Task QuoteAsync_RejectsItemUsedAsNewExchangeItem()
    {
        await using var fixture = await RefundTestData.CreateIssuedOrderAsync();
        fixture.Db.Add(new ExchangeItem
        {
            ExchangeItemId = 602,
            ExchangeId = 702,
            OrderItemId = 999,
            NewOrderItemId = fixture.OrderItemIds[0],
        });
        await fixture.Db.SaveChangesAsync();

        var result = await QuoteFirstItemAsync(fixture);

        AssertConflict(result, "REFUND_ITEM_EXCHANGE_CONFLICT");
    }

    [Fact]
    public async Task QuoteAsync_IgnoresDisabledPoliciesAndReturnsNotFound()
    {
        await using var fixture = await RefundTestData.CreateIssuedOrderAsync();
        fixture.Db.Add(Policy(status: 0));
        await fixture.Db.SaveChangesAsync();

        var result = await QuoteFirstItemAsync(fixture);

        AssertConflict(result, "REFUND_POLICY_NOT_FOUND");
    }

    [Fact]
    public async Task QuoteAsync_IgnoresEnabledPolicyForAnotherShow()
    {
        await using var fixture = await RefundTestData.CreateIssuedOrderAsync();
        fixture.Db.Add(Policy(showId: 999));
        await fixture.Db.SaveChangesAsync();

        var result = await QuoteFirstItemAsync(fixture);

        AssertConflict(result, "REFUND_POLICY_NOT_FOUND");
    }

    [Fact]
    public async Task QuoteAsync_RejectsNonPositiveActualRefund()
    {
        await using var fixture = await RefundTestData.CreateIssuedOrderAsync();
        fixture.Db.Add(Policy(refundRate: 0m, serviceFee: 1m));
        await fixture.Db.SaveChangesAsync();

        var result = await QuoteFirstItemAsync(fixture);

        AssertConflict(result, "REFUND_AMOUNT_NOT_POSITIVE");
    }

    [Fact]
    public async Task QuoteAsync_SortsResponseItemsByOrderItemId()
    {
        await using var fixture = await RefundTestData.CreateIssuedOrderAsync();
        fixture.Db.Add(Policy());
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.CreateApplicationService().QuoteAsync(
            fixture.UserId,
            fixture.OrderId,
            new RefundQuoteRequest(fixture.OrderItemIds.Reverse().ToArray()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(fixture.OrderItemIds, result.Value!.Items.Select(x => x.OrderItemId));
    }

    [Fact]
    public async Task QuoteAsync_DoesNotTrackOrWriteDatabaseEntities()
    {
        await using var fixture = await RefundTestData.CreateIssuedOrderAsync();
        fixture.Db.Add(Policy());
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var beforeCounts = await DatabaseCountsAsync(fixture);

        var result = await QuoteFirstItemAsync(fixture);

        Assert.True(result.IsSuccess);
        Assert.Empty(fixture.Db.ChangeTracker.Entries());
        Assert.Equal(beforeCounts, await DatabaseCountsAsync(fixture));
    }

    [Fact]
    public async Task QuoteAsync_OwnershipValidationPrecedesMalformedItemIds()
    {
        await using var fixture = await RefundTestData.CreateIssuedOrderAsync();
        var itemId = fixture.OrderItemIds[0];

        var result = await fixture.CreateApplicationService().QuoteAsync(
            fixture.UserId + 1,
            fixture.OrderId,
            new RefundQuoteRequest([itemId, itemId]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderTicketFailure.NotFound, result.Failure);
        Assert.Equal("REFUND_ORDER_NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public async Task QuoteAsync_MalformedItemIdsPrecedeInvalidOrderAndSession()
    {
        await using var fixture = await RefundTestData.CreateIssuedOrderAsync();
        var order = await fixture.Db.Set<Order>().SingleAsync();
        var session = await fixture.Db.Set<ShowSession>().SingleAsync();
        order.OrderStatus = "PAID";
        session.StartTime = RefundTestData.FixedUtcNow;
        await fixture.Db.SaveChangesAsync();
        var itemId = fixture.OrderItemIds[0];

        var result = await fixture.CreateApplicationService().QuoteAsync(
            fixture.UserId,
            fixture.OrderId,
            new RefundQuoteRequest([itemId, itemId]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderTicketFailure.InvalidRequest, result.Failure);
        Assert.Equal("REFUND_ITEM_IDS_DUPLICATED", result.ErrorCode);
    }

    [Fact]
    public async Task QuoteAsync_InvalidOrderPrecedesItemConflict()
    {
        await using var fixture = await RefundTestData.CreateIssuedOrderAsync();
        var order = await fixture.Db.Set<Order>().SingleAsync();
        var item = await fixture.Db.Set<OrderItem>()
            .SingleAsync(x => x.OrderItemId == fixture.OrderItemIds[0]);
        order.OrderStatus = "CANCELLED";
        item.ItemStatus = "REFUNDED";
        await fixture.Db.SaveChangesAsync();

        var result = await QuoteFirstItemAsync(fixture);

        AssertConflict(result, "REFUND_ORDER_NOT_ELIGIBLE");
    }

    [Fact]
    public async Task QuoteAsync_StartedSessionPrecedesItemConflict()
    {
        await using var fixture = await RefundTestData.CreateIssuedOrderAsync();
        var session = await fixture.Db.Set<ShowSession>().SingleAsync();
        var item = await fixture.Db.Set<OrderItem>()
            .SingleAsync(x => x.OrderItemId == fixture.OrderItemIds[0]);
        session.StartTime = RefundTestData.FixedUtcNow;
        item.ItemStatus = "REFUNDED";
        await fixture.Db.SaveChangesAsync();

        var result = await QuoteFirstItemAsync(fixture);

        AssertConflict(result, "REFUND_SESSION_STARTED");
    }

    [Fact]
    public async Task QuoteAsync_MissingSessionPrecedesItemConflict()
    {
        await using var fixture = await RefundTestData.CreateIssuedOrderAsync();
        var order = await fixture.Db.Set<Order>().SingleAsync();
        var item = await fixture.Db.Set<OrderItem>()
            .SingleAsync(x => x.OrderItemId == fixture.OrderItemIds[0]);
        order.SessionId = 999;
        item.ItemStatus = "REFUNDED";
        await fixture.Db.SaveChangesAsync();

        var result = await QuoteFirstItemAsync(fixture);

        AssertConflict(result, "REFUND_SESSION_INVALID");
    }

    [Fact]
    public async Task QuoteAsync_ItemConflictPrecedesTicketConflict()
    {
        await using var fixture = await RefundTestData.CreateIssuedOrderAsync();
        var item = await fixture.Db.Set<OrderItem>()
            .SingleAsync(x => x.OrderItemId == fixture.OrderItemIds[0]);
        var ticket = await fixture.Db.Set<ETicket>()
            .SingleAsync(x => x.OrderItemId == fixture.OrderItemIds[0]);
        item.ItemStatus = "REFUNDING";
        ticket.TicketStatus = "USED";
        await fixture.Db.SaveChangesAsync();

        var result = await QuoteFirstItemAsync(fixture);

        AssertConflict(result, "REFUND_ITEM_NOT_ELIGIBLE");
    }

    [Fact]
    public async Task QuoteAsync_TicketConflictPrecedesReservationConflict()
    {
        await using var fixture = await RefundTestData.CreateIssuedOrderAsync();
        var ticket = await fixture.Db.Set<ETicket>()
            .SingleAsync(x => x.OrderItemId == fixture.OrderItemIds[0]);
        var reservation = await fixture.Db.Set<SeatReservation>()
            .SingleAsync(x => x.OrderItemId == fixture.OrderItemIds[0]);
        ticket.TicketStatus = "USED";
        reservation.ReservationStatus = "RELEASED";
        await fixture.Db.SaveChangesAsync();

        var result = await QuoteFirstItemAsync(fixture);

        AssertConflict(result, "REFUND_TICKET_NOT_UNUSED");
    }

    [Fact]
    public async Task QuoteAsync_ReservationConflictPrecedesHistoryConflict()
    {
        await using var fixture = await RefundTestData.CreateIssuedOrderAsync();
        var reservation = await fixture.Db.Set<SeatReservation>()
            .SingleAsync(x => x.OrderItemId == fixture.OrderItemIds[0]);
        reservation.ReservationStatus = "RELEASED";
        fixture.Db.Add(RefundRelation(fixture.OrderItemIds[0]));
        await fixture.Db.SaveChangesAsync();

        var result = await QuoteFirstItemAsync(fixture);

        AssertConflict(result, "REFUND_RESERVATION_DATA_INCONSISTENT");
    }

    [Fact]
    public async Task QuoteAsync_HistoryConflictPrecedesPaymentInconsistency()
    {
        await using var fixture = await RefundTestData.CreateIssuedOrderAsync();
        fixture.Db.Add(RefundRelation(fixture.OrderItemIds[0]));
        (await fixture.Db.Set<Payment>().SingleAsync()).PayAmount = 209m;
        await fixture.Db.SaveChangesAsync();

        var result = await QuoteFirstItemAsync(fixture);

        AssertConflict(result, "REFUND_ITEM_ALREADY_REQUESTED");
    }

    [Fact]
    public async Task QuoteAsync_RefundRelationPrecedesExchangeConflict()
    {
        await using var fixture = await RefundTestData.CreateIssuedOrderAsync();
        fixture.Db.Add(RefundRelation(fixture.OrderItemIds[0]));
        fixture.Db.Add(new ExchangeItem
        {
            ExchangeItemId = 603,
            ExchangeId = 703,
            OrderItemId = fixture.OrderItemIds[0],
            NewOrderItemId = 999,
        });
        await fixture.Db.SaveChangesAsync();

        var result = await QuoteFirstItemAsync(fixture);

        AssertConflict(result, "REFUND_ITEM_ALREADY_REQUESTED");
    }

    [Fact]
    public async Task QuoteAsync_PaymentInconsistencyPrecedesMissingPolicy()
    {
        await using var fixture = await RefundTestData.CreateIssuedOrderAsync();
        (await fixture.Db.Set<Payment>().SingleAsync()).PayAmount = 209m;
        await fixture.Db.SaveChangesAsync();

        var result = await QuoteFirstItemAsync(fixture);

        AssertConflict(result, "REFUND_PAYMENT_DATA_INCONSISTENT");
    }

    private static async Task<RefundTestData> CreatePaymentFaultFixtureAsync(PaymentFault fault)
    {
        var fixture = fault switch
        {
            PaymentFault.NonPositivePayAmount =>
                await RefundTestData.CreateIssuedOrderAsync(300m, 300m, 0m, [300m]),
            PaymentFault.PaymentDoesNotMatchOrderNet =>
                await RefundTestData.CreateIssuedOrderAsync(300m, 60m, 239m, [100m, 200m]),
            PaymentFault.ItemSumDoesNotMatchTotal =>
                await RefundTestData.CreateIssuedOrderAsync(300m, 60m, 240m, [100m, 190m]),
            PaymentFault.ZeroDenominator =>
                await RefundTestData.CreateIssuedOrderAsync(0m, 0m, 1m, [0m]),
            _ => await RefundTestData.CreateIssuedOrderAsync(),
        };

        if (fault == PaymentFault.NoSuccessfulPayment)
        {
            (await fixture.Db.Set<Payment>().SingleAsync()).PayStatus = "PENDING";
        }
        else if (fault == PaymentFault.TwoSuccessfulPayments)
        {
            fixture.Db.Add(new Payment
            {
                PaymentId = 32,
                PaymentNo = "PAY000032",
                OrderId = fixture.OrderId,
                UserId = fixture.UserId,
                PayAmount = 210m,
                PayChannel = "WECHAT",
                PayStatus = "SUCCESS",
                PayTime = RefundTestData.FixedUtcNow.AddHours(-1),
            });
        }

        await fixture.Db.SaveChangesAsync();
        return fixture;
    }

    private static async Task<OrderTicketResult<RefundQuoteResponse>> QuoteFirstItemAsync(
        RefundTestData fixture) =>
        await fixture.CreateApplicationService().QuoteAsync(
            fixture.UserId,
            fixture.OrderId,
            new RefundQuoteRequest([fixture.OrderItemIds[0]]),
            CancellationToken.None);

    private static void AssertConflict(
        OrderTicketResult<RefundQuoteResponse> result,
        string errorCode)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(OrderTicketFailure.Conflict, result.Failure);
        Assert.Equal(errorCode, result.ErrorCode);
    }

    private static RefundPolicy Policy(
        decimal refundRate = 1m,
        decimal serviceFee = 0m,
        byte status = 1,
        long? showId = null) => new()
    {
        PolicyId = 801,
        ShowId = showId,
        PolicyName = "全局",
        RefundDeadlineHour = 24,
        RefundRate = refundRate,
        ServiceFee = serviceFee,
        Priority = 1,
        Status = status,
    };

    private static RefundItem RefundRelation(long orderItemId) => new()
    {
        RefundItemId = 401,
        RefundId = 501,
        OrderItemId = orderItemId,
        RefundBaseAmount = 105m,
    };

    private static async Task<(int Refunds, int RefundItems, int Exchanges, int ExchangeItems)>
        DatabaseCountsAsync(RefundTestData fixture) => (
            await fixture.Db.Set<RefundRequest>().CountAsync(),
            await fixture.Db.Set<RefundItem>().CountAsync(),
            await fixture.Db.Set<ExchangeRequest>().CountAsync(),
            await fixture.Db.Set<ExchangeItem>().CountAsync());

    public enum PaymentFault
    {
        NoSuccessfulPayment,
        TwoSuccessfulPayments,
        NonPositivePayAmount,
        PaymentDoesNotMatchOrderNet,
        ItemSumDoesNotMatchTotal,
        ZeroDenominator,
    }
}
