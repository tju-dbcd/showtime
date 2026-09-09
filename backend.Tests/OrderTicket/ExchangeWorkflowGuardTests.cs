using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ShowtimeBackend.Common;
using ShowtimeBackend.Common.TicketSecurity;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Services.OrderTicket;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class ExchangeWorkflowGuardTests
{
    [Fact]
    public async Task GeneralPayment_RejectsExchangeChildOrder()
    {
        await using var fixture = await CreateExchangeOrderAsync();
        var service = new PaymentService(
            fixture.Db, fixture.TimeProvider, new TicketIssuanceService(new TokenService()),
            NullLogger<PaymentService>.Instance, new NullOrderTicketAuditSink(),
            new OrderExpirationService(
                fixture.Db,
                fixture.TimeProvider,
                Options.Create(new OrderExpirationOptions()),
                NullLogger<OrderExpirationService>.Instance));

        var result = await service.PayAsync(7, "alice", 11,
            new MockPaymentRequest(PaymentChannel.ALIPAY, PaymentResult.SUCCESS), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("EXCHANGE_PAYMENT_REQUIRES_WORKFLOW", result.ErrorCode);
    }

    [Fact]
    public async Task UserAndAdminCancellation_RejectExchangeChildOrder()
    {
        await using var fixture = await CreateExchangeOrderAsync();
        var service = new OrderService(fixture.Db, fixture.TimeProvider);

        var user = await service.CancelAsync(7, "alice", 11, CancellationToken.None);
        fixture.Db.ChangeTracker.Clear();
        var admin = await service.CancelAdminAsync("admin", 11, CancellationToken.None);

        Assert.Equal("EXCHANGE_CANCEL_REQUIRES_WORKFLOW", user.ErrorCode);
        Assert.Equal("EXCHANGE_CANCEL_REQUIRES_WORKFLOW", admin.ErrorCode);
    }

    [Fact]
    public async Task AdminIssuance_RejectsExchangeChildOrder()
    {
        await using var fixture = await CreateExchangeOrderAsync();
        var service = new AdminTicketIssuanceService(
            fixture.Db, fixture.TimeProvider, new TicketIssuanceService(new TokenService()),
            NullLogger<AdminTicketIssuanceService>.Instance, new NullOrderTicketAuditSink());

        var result = await service.IssueAsync("admin", 11, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("EXCHANGE_ISSUANCE_REQUIRES_WORKFLOW", result.ErrorCode);
    }

    private static async Task<RefundTestData> CreateExchangeOrderAsync()
    {
        var fixture = await RefundTestData.CreateIssuedOrderAsync();
        var order = await fixture.Db.Set<ShowtimeBackend.Entities.OrderTicket.Order>()
            .SingleAsync(item => item.OrderId == fixture.OrderId);
        order.OrderType = "EXCHANGE";
        order.ParentOrderId = 10;
        order.OrderStatus = "PENDING_PAY";
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        return fixture;
    }

    private sealed class TokenService : ITicketTokenService
    {
        public TicketCredential Generate(DateTimeOffset issuedAt) => new("guard", "guard", "guard");
        public bool TryValidate(string qrCode, out TicketTokenPayload? payload)
        {
            payload = null;
            return false;
        }
    }
}
