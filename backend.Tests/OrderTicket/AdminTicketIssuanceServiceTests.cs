using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ShowtimeBackend.Common.TicketSecurity;
using ShowtimeBackend.Data;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Services.OrderTicket;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class AdminTicketIssuanceServiceTests
{
    private static readonly DateTimeOffset OperationTime =
        new(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task IssueAsync_ForHistoricalPaidOrder_CreatesAllTicketsAndMarksIssued()
    {
        await using var connection = await CreateConnectionAsync();
        await using var db = await CreateDbContextAsync(connection);
        db.Add(CreateOrder("PAID", itemCount: 2, includeSuccessfulPayment: true));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.IssueAsync("admin", 10, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("ISSUED", result.Value!.OrderStatus.ToString());
        Assert.Equal(2, result.Value.CreatedTicketCount);
        Assert.Equal(0, result.Value.ExistingTicketCount);
        Assert.Equal(2, result.Value.TotalTicketCount);
        Assert.Equal(OperationTime.UtcDateTime, result.Value.IssueTime);
        Assert.Equal(2, await db.Set<ETicket>().CountAsync());
        var savedOrder = await db.Set<Order>().SingleAsync();
        Assert.Equal("ISSUED", savedOrder.OrderStatus);
        Assert.Equal(OperationTime.UtcDateTime, savedOrder.IssueTime);
    }

    [Fact]
    public async Task IssueAsync_ForCompleteIssuedOrder_IsIdempotent()
    {
        await using var connection = await CreateConnectionAsync();
        await using var db = await CreateDbContextAsync(connection);
        var order = CreateOrder("ISSUED", itemCount: 1, includeSuccessfulPayment: true);
        order.IssueTime = OperationTime.UtcDateTime.AddDays(-1);
        AttachTicket(order.Items.Single(), order.UserId, "existing-qr");
        db.Add(order);
        await db.SaveChangesAsync();
        var originalIssueTime = order.IssueTime;
        var service = CreateService(db);

        var result = await service.IssueAsync("admin", 10, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.CreatedTicketCount);
        Assert.Equal(1, result.Value.ExistingTicketCount);
        Assert.Equal(originalIssueTime, result.Value.IssueTime);
        Assert.Equal("existing-qr", (await db.Set<ETicket>().SingleAsync()).QrCode);
    }

    [Fact]
    public async Task IssueAsync_ForIncompleteIssuedOrder_RepairsOnlyMissingTicket()
    {
        await using var connection = await CreateConnectionAsync();
        await using var db = await CreateDbContextAsync(connection);
        var order = CreateOrder("ISSUED", itemCount: 2, includeSuccessfulPayment: true);
        order.IssueTime = OperationTime.UtcDateTime.AddDays(-1);
        AttachTicket(order.Items.OrderBy(item => item.OrderItemId).First(), order.UserId, "existing-qr");
        db.Add(order);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.IssueAsync("admin", 10, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.CreatedTicketCount);
        Assert.Equal(1, result.Value.ExistingTicketCount);
        Assert.Equal(2, await db.Set<ETicket>().CountAsync());
        Assert.Equal(
            "existing-qr",
            (await db.Set<ETicket>().SingleAsync(ticket => ticket.OrderItemId == 1)).QrCode);
    }

    [Theory]
    [InlineData("PENDING_PAY", true, "TICKET_ORDER_NOT_ISSUABLE")]
    [InlineData("CANCELLED", true, "TICKET_ORDER_NOT_ISSUABLE")]
    [InlineData("PAID", false, "TICKET_SUCCESSFUL_PAYMENT_REQUIRED")]
    public async Task IssueAsync_RejectsInvalidCompensationData(
        string status,
        bool includeSuccessfulPayment,
        string expectedCode)
    {
        await using var connection = await CreateConnectionAsync();
        await using var db = await CreateDbContextAsync(connection);
        db.Add(CreateOrder(status, itemCount: 1, includeSuccessfulPayment));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.IssueAsync("admin", 10, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderTicketFailure.Conflict, result.Failure);
        Assert.Equal(expectedCode, result.ErrorCode);
        Assert.Empty(await db.Set<ETicket>().ToListAsync());
    }

    [Fact]
    public async Task IssueAsync_ForMissingOrder_ReturnsNotFound()
    {
        await using var connection = await CreateConnectionAsync();
        await using var db = await CreateDbContextAsync(connection);

        var result = await CreateService(db).IssueAsync(
            "admin",
            999,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderTicketFailure.NotFound, result.Failure);
        Assert.Equal("TICKET_ORDER_NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public async Task IssueAsync_WhenTicketNumberCollides_RegeneratesOnceAndSucceeds()
    {
        await using var connection = await CreateConnectionAsync();
        await using var db = await CreateDbContextAsync(connection);
        db.Add(CreateOrder("PAID", itemCount: 1, includeSuccessfulPayment: true));
        db.Add(new ETicket
        {
            ETicketId = 999,
            ETicketNo = "TKT-COLLISION",
            OrderItemId = 999,
            UserId = 99,
            QrCode = "qr-existing",
            AntiFakeCode = "anti-existing",
            TicketStatus = "UNUSED",
        });
        await db.SaveChangesAsync();
        var tokenService = new SequenceTokenService(
            new TicketCredential("TKT-COLLISION", "anti-first", "qr-first"),
            new TicketCredential("TKT-NEW", "anti-second", "qr-second"));
        var service = CreateService(db, tokenService);

        var result = await service.IssueAsync("admin", 10, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, tokenService.GenerateCount);
        var created = await db.Set<ETicket>()
            .SingleAsync(ticket => ticket.OrderItemId == 1);
        Assert.Equal("TKT-NEW", created.ETicketNo);
        Assert.Equal("ISSUED", (await db.Set<Order>().SingleAsync()).OrderStatus);
    }

    [Fact]
    public async Task IssueAsync_WhenGeneratedIdentifierCollidesTwice_ReturnsFailure()
    {
        await using var connection = await CreateConnectionAsync();
        await using var db = await CreateDbContextAsync(connection);
        db.Add(CreateOrder("PAID", itemCount: 1, includeSuccessfulPayment: true));
        db.Add(new ETicket
        {
            ETicketId = 999,
            ETicketNo = "TKT-COLLISION",
            OrderItemId = 999,
            UserId = 99,
            QrCode = "qr-existing",
            AntiFakeCode = "anti-existing",
            TicketStatus = "UNUSED",
        });
        await db.SaveChangesAsync();
        var tokenService = new SequenceTokenService(
            new TicketCredential("TKT-COLLISION", "anti-first", "qr-first"),
            new TicketCredential("TKT-COLLISION", "anti-second", "qr-second"));

        var result = await CreateService(db, tokenService).IssueAsync(
            "admin",
            10,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderTicketFailure.Internal, result.Failure);
        Assert.Equal("TICKET_ISSUANCE_FAILED", result.ErrorCode);
        Assert.Equal(2, tokenService.GenerateCount);
        await using var verificationDb = await CreateDbContextAsync(connection);
        Assert.Equal("PAID", (await verificationDb.Set<Order>().SingleAsync()).OrderStatus);
        Assert.Empty(await verificationDb.Set<ETicket>()
            .Where(ticket => ticket.OrderItemId == 1)
            .ToListAsync());
    }

    private static AdminTicketIssuanceService CreateService(
        AppDbContext db,
        ITicketTokenService? tokenService = null) => new(
        db,
        new FixedTimeProvider(OperationTime),
        new TicketIssuanceService(
            tokenService ?? new HmacTicketTokenService(
                Options.Create(new TicketSecurityOptions
                {
                    SigningKeyBase64 =
                        "ERERERERERERERERERERERERERERERERERERERERERE=",
                }))),
        NullLogger<AdminTicketIssuanceService>.Instance,
        new NullOrderTicketAuditSink());

    private static async Task<SqliteConnection> CreateConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<AppDbContext> CreateDbContextAsync(
        SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<SqliteAuthDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new SqliteAuthDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
        return db;
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
                PaymentId = 20,
                PaymentNo = "PAY000020",
                OrderId = order.OrderId,
                UserId = order.UserId,
                PayAmount = order.TotalAmount,
                PayChannel = "ALIPAY",
                PayStatus = "SUCCESS",
                PayTime = OperationTime.UtcDateTime.AddMinutes(-1),
                Order = order,
            });
        }

        return order;
    }

    private static void AttachTicket(
        OrderItem item,
        long userId,
        string qrCode)
    {
        item.ETicket = new ETicket
        {
            ETicketId = 100 + item.OrderItemId,
            ETicketNo = $"TKT-EXISTING-{item.OrderItemId}",
            OrderItemId = item.OrderItemId,
            UserId = userId,
            QrCode = qrCode,
            AntiFakeCode = $"ANTI-{item.OrderItemId}",
            TicketStatus = "UNUSED",
            OrderItem = item,
        };
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class SequenceTokenService(params TicketCredential[] credentials)
        : ITicketTokenService
    {
        private readonly Queue<TicketCredential> _credentials = new(credentials);

        public int GenerateCount { get; private set; }

        public TicketCredential Generate(DateTimeOffset issuedAt)
        {
            GenerateCount++;
            return _credentials.Dequeue();
        }

        public bool TryValidate(string qrCode, out TicketTokenPayload? payload)
        {
            payload = null;
            return false;
        }
    }
}
