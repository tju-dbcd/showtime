using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;
using ShowtimeBackend.Common;
using ShowtimeBackend.Common.TicketSecurity;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.OrderTicket;
using ShowSessionEntity = ShowtimeBackend.Entities.ShowSession.ShowSession;
using ShowtimeBackend.Services.OrderTicket;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class TicketRedemptionServiceTests
{
    private static readonly DateTimeOffset OperationTime =
        new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RedeemAsync_WhenEligible_WritesFirstRedemptionAndAudit()
    {
        await using var fixture = await RedemptionFixture.CreateAsync();
        var audit = new RecordingAuditSink();

        var result = await fixture.CreateService(audit).RedeemAsync(
            "admin",
            new RedeemTicketRequest(fixture.Credential.QrCode, " gate-a-01 "),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ETicketStatus.USED, result.Value!.TicketStatus);
        Assert.Equal(OperationTime.UtcDateTime, result.Value.CheckTime);
        Assert.Equal(DateTimeKind.Utc, result.Value.CheckTime.Kind);
        Assert.Equal("gate-a-01", result.Value.CheckDevice);
        Assert.Equal("admin", result.Value.CheckBy);
        var persisted = await fixture.Db.Set<ETicket>().AsNoTracking().SingleAsync();
        Assert.Equal("USED", persisted.TicketStatus);
        Assert.Equal("gate-a-01", persisted.CheckDevice);
        Assert.Equal("admin", persisted.CheckBy);
        Assert.Equal("admin", persisted.UpdateBy);
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("TICKET_REDEEMED", auditEvent.Operation);
        Assert.DoesNotContain(fixture.Credential.QrCode, auditEvent.Metadata!.Values);
        Assert.DoesNotContain(fixture.Credential.AntiFakeCode, auditEvent.Metadata.Values);
    }

    [Theory]
    [InlineData(null, "gate", "TICKET_QR_INVALID")]
    [InlineData("", "gate", "TICKET_QR_INVALID")]
    [InlineData("   ", "gate", "TICKET_QR_INVALID")]
    [InlineData("invalid", null, "TICKET_QR_INVALID")]
    public async Task RedeemAsync_WhenRequestIsInvalid_UsesStablePriority(
        string? qrCode,
        string? checkDevice,
        string expectedCode)
    {
        await using var fixture = await RedemptionFixture.CreateAsync();

        var result = await fixture.CreateService().RedeemAsync(
            "admin",
            new RedeemTicketRequest(qrCode, checkDevice),
            CancellationToken.None);

        Assert.Equal(OrderTicketFailure.InvalidRequest, result.Failure);
        Assert.Equal(expectedCode, result.ErrorCode);
        Assert.Equal("UNUSED", await fixture.TicketStatusAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RedeemAsync_WhenDeviceIsInvalid_ReturnsDeviceError(string? device)
    {
        await using var fixture = await RedemptionFixture.CreateAsync();

        var result = await fixture.CreateService().RedeemAsync(
            "admin",
            new RedeemTicketRequest(fixture.Credential.QrCode, device),
            CancellationToken.None);

        Assert.Equal("TICKET_DEVICE_INVALID", result.ErrorCode);
    }

    [Fact]
    public async Task RedeemAsync_WhenFieldsExceedStorageLimits_ReturnsDomainErrors()
    {
        await using var fixture = await RedemptionFixture.CreateAsync();

        var invalidQr = await fixture.CreateService().RedeemAsync(
            "admin",
            new RedeemTicketRequest(new string('q', 256), "gate"),
            CancellationToken.None);
        var invalidDevice = await fixture.CreateService().RedeemAsync(
            "admin",
            new RedeemTicketRequest(fixture.Credential.QrCode, new string('d', 101)),
            CancellationToken.None);

        Assert.Equal("TICKET_QR_INVALID", invalidQr.ErrorCode);
        Assert.Equal("TICKET_DEVICE_INVALID", invalidDevice.ErrorCode);
    }

    [Theory]
    [InlineData(-1, 120)]
    [InlineData(120, -1)]
    [InlineData(10081, 120)]
    [InlineData(120, 10081)]
    public void TicketRedemptionOptions_RejectValuesOutsideSevenDays(
        int openBefore,
        int closeAfter)
    {
        var options = new TicketRedemptionOptions
        {
            OpenBeforeMinutes = openBefore,
            CloseAfterMinutes = closeAfter,
        };

        Assert.False(Validator.TryValidateObject(
            options,
            new ValidationContext(options),
            [],
            validateAllProperties: true));
    }

    [Theory]
    [InlineData("USED", "TICKET_ALREADY_USED")]
    [InlineData("REFUNDING", "TICKET_REFUNDING")]
    [InlineData("REFUNDED", "TICKET_REFUNDED")]
    [InlineData("EXCHANGED", "TICKET_EXCHANGED")]
    public async Task RedeemAsync_WhenTicketStateIsIneligible_ReturnsSpecificConflict(
        string status,
        string expectedCode)
    {
        await using var fixture = await RedemptionFixture.CreateAsync();
        await fixture.SetTicketAsync(ticket => ticket.TicketStatus = status);

        var result = await fixture.RedeemAsync();

        Assert.Equal(OrderTicketFailure.Conflict, result.Failure);
        Assert.Equal(expectedCode, result.ErrorCode);
    }

    [Fact]
    public async Task RedeemAsync_WhenRepeated_DoesNotOverwriteFirstCheckFields()
    {
        await using var fixture = await RedemptionFixture.CreateAsync();
        var first = await fixture.RedeemAsync("admin-1", "gate-1");

        var second = await fixture.RedeemAsync("admin-2", "gate-2");

        Assert.True(first.IsSuccess);
        Assert.Equal("TICKET_ALREADY_USED", second.ErrorCode);
        var persisted = await fixture.Db.Set<ETicket>().AsNoTracking().SingleAsync();
        Assert.Equal("admin-1", persisted.CheckBy);
        Assert.Equal("gate-1", persisted.CheckDevice);
        Assert.Equal(first.Value!.CheckTime, DateTime.SpecifyKind(
            persisted.CheckTime!.Value,
            DateTimeKind.Utc));
    }

    [Theory]
    [InlineData("time")]
    [InlineData("device")]
    [InlineData("actor")]
    public async Task RedeemAsync_WhenUnusedTicketHasPartialCheckData_ReturnsConflict(
        string field)
    {
        await using var fixture = await RedemptionFixture.CreateAsync();
        await fixture.SetTicketAsync(ticket =>
        {
            if (field == "time") ticket.CheckTime = OperationTime.UtcDateTime;
            if (field == "device") ticket.CheckDevice = "gate-old";
            if (field == "actor") ticket.CheckBy = "admin-old";
        });

        var result = await fixture.RedeemAsync();

        Assert.Equal("TICKET_REDEMPTION_CONFLICT", result.ErrorCode);
        var persisted = await fixture.Db.Set<ETicket>().AsNoTracking().SingleAsync();
        Assert.Equal("UNUSED", persisted.TicketStatus);
        if (field == "device") Assert.Equal("gate-old", persisted.CheckDevice);
        if (field == "actor") Assert.Equal("admin-old", persisted.CheckBy);
    }

    [Theory]
    [InlineData("PAID", "NORMAL", "TICKET_ORDER_NOT_ELIGIBLE")]
    [InlineData("ISSUED", "REFUNDING", "TICKET_ITEM_NOT_ELIGIBLE")]
    public async Task RedeemAsync_WhenAggregateIsIneligible_ReturnsConflict(
        string orderStatus,
        string itemStatus,
        string expectedCode)
    {
        await using var fixture = await RedemptionFixture.CreateAsync();
        await fixture.SetAggregateStatusAsync(orderStatus, itemStatus);

        var result = await fixture.RedeemAsync();

        Assert.Equal(expectedCode, result.ErrorCode);
        Assert.Equal("UNUSED", await fixture.TicketStatusAsync());
    }

    [Theory]
    [InlineData(120, 180, true)]
    [InlineData(121, 180, false)]
    [InlineData(-180, -120, true)]
    [InlineData(-180, -121, false)]
    public async Task RedeemAsync_UsesInclusiveConfiguredWindow(
        int startOffsetMinutes,
        int endOffsetMinutes,
        bool succeeds)
    {
        await using var fixture = await RedemptionFixture.CreateAsync(
            OperationTime.UtcDateTime.AddMinutes(startOffsetMinutes),
            OperationTime.UtcDateTime.AddMinutes(endOffsetMinutes));

        var result = await fixture.RedeemAsync();

        Assert.Equal(succeeds, result.IsSuccess);
        if (!succeeds)
        {
            Assert.Equal("TICKET_REDEMPTION_WINDOW_INVALID", result.ErrorCode);
        }
    }

    [Fact]
    public async Task RedeemAsync_WhenAuditFails_KeepsSuccessfulRedemption()
    {
        await using var fixture = await RedemptionFixture.CreateAsync();

        var result = await fixture.CreateService(new ThrowingAuditSink()).RedeemAsync(
            "admin",
            new RedeemTicketRequest(fixture.Credential.QrCode, "gate"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("USED", await fixture.TicketStatusAsync());
    }

    private sealed class RedemptionFixture : IAsyncDisposable
    {
        private const string SigningKey =
            "ERERERERERERERERERERERERERERERERERERERERERE=";
        private readonly SqliteConnection connection;
        private readonly ITicketTokenService tokenService;

        private RedemptionFixture(
            SqliteConnection connection,
            AppDbContext db,
            ITicketTokenService tokenService,
            TicketCredential credential)
        {
            this.connection = connection;
            Db = db;
            this.tokenService = tokenService;
            Credential = credential;
        }

        public AppDbContext Db { get; }
        public TicketCredential Credential { get; }

        public static async Task<RedemptionFixture> CreateAsync(
            DateTime? sessionStart = null,
            DateTime? sessionEnd = null)
        {
            var connection = new SqliteConnection(
                "Data Source=:memory:;Foreign Keys=False");
            await connection.OpenAsync();
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA foreign_keys = OFF;";
                await command.ExecuteNonQueryAsync();
            }
            var options = new DbContextOptionsBuilder<SqliteAuthDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new SqliteAuthDbContext(options);
            await db.Database.EnsureCreatedAsync();
            var tokenService = new HmacTicketTokenService(
                Options.Create(new TicketSecurityOptions
                {
                    SigningKeyBase64 = SigningKey,
                }));
            var credential = tokenService.Generate(OperationTime);
            db.AddRange(
                new ShowSessionEntity
                {
                    SessionId = 21,
                    ShowId = 90,
                    SeatMapId = 30,
                    StartTime = sessionStart ?? OperationTime.UtcDateTime.AddHours(-1),
                    EndTime = sessionEnd ?? OperationTime.UtcDateTime.AddHours(1),
                    SaleStartTime = OperationTime.UtcDateTime.AddDays(-10),
                    SaleEndTime = OperationTime.UtcDateTime.AddDays(-1),
                    SessionStatus = "ENDED",
                },
                new Order
                {
                    OrderId = 11,
                    OrderNo = "ORD000011",
                    UserId = 7,
                    SessionId = 21,
                    TotalAmount = 188m,
                    TicketCount = 1,
                    OrderStatus = "ISSUED",
                    ExpireTime = OperationTime.UtcDateTime.AddDays(-1),
                    IssueTime = OperationTime.UtcDateTime.AddDays(-1),
                    Source = "WEB",
                },
                new OrderItem
                {
                    OrderItemId = 101,
                    OrderId = 11,
                    SeatId = 501,
                    PriceStrategyId = 601,
                    UnitPrice = 188m,
                    ItemStatus = "NORMAL",
                },
                new ETicket
                {
                    ETicketId = 201,
                    ETicketNo = credential.TicketNo,
                    OrderItemId = 101,
                    UserId = 7,
                    QrCode = credential.QrCode,
                    AntiFakeCode = credential.AntiFakeCode,
                    TicketStatus = "UNUSED",
                });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return new RedemptionFixture(connection, db, tokenService, credential);
        }

        public TicketRedemptionService CreateService(
            IOrderTicketAuditSink? auditSink = null) => new(
            Db,
            tokenService,
            new FixedTimeProvider(OperationTime),
            Options.Create(new TicketRedemptionOptions()),
            NullLogger<TicketRedemptionService>.Instance,
            auditSink ?? new NullOrderTicketAuditSink());

        public Task<OrderTicketResult<TicketRedemptionResponse>> RedeemAsync(
            string actor = "admin",
            string device = "gate") => CreateService().RedeemAsync(
            actor,
            new RedeemTicketRequest(Credential.QrCode, device),
            CancellationToken.None);

        public async Task SetTicketAsync(Action<ETicket> mutate)
        {
            var ticket = await Db.Set<ETicket>().SingleAsync();
            mutate(ticket);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public async Task SetAggregateStatusAsync(string orderStatus, string itemStatus)
        {
            var order = await Db.Set<Order>().SingleAsync();
            var item = await Db.Set<OrderItem>().SingleAsync();
            order.OrderStatus = orderStatus;
            item.ItemStatus = itemStatus;
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public Task<string> TicketStatusAsync() => Db.Set<ETicket>()
            .AsNoTracking()
            .Select(ticket => ticket.TicketStatus)
            .SingleAsync();

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingAuditSink : IOrderTicketAuditSink
    {
        public List<OrderTicketAuditEvent> Events { get; } = [];

        public ValueTask WriteAsync(
            OrderTicketAuditEvent auditEvent,
            CancellationToken cancellationToken)
        {
            Events.Add(auditEvent);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingAuditSink : IOrderTicketAuditSink
    {
        public ValueTask WriteAsync(
            OrderTicketAuditEvent auditEvent,
            CancellationToken cancellationToken) =>
            ValueTask.FromException(new InvalidOperationException("audit failed"));
    }
}
