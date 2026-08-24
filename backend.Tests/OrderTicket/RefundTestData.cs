using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ShowtimeBackend.Data;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Entities.SeatZone;
using ShowtimeBackend.Entities.ShowSession;
using ShowtimeBackend.Services.OrderTicket;

namespace ShowtimeBackend.Tests.OrderTicket;

internal sealed class RefundTestData : IAsyncDisposable
{
    public static readonly DateTime FixedUtcNow =
        new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection;

    private RefundTestData(
        SqliteConnection connection,
        AppDbContext db,
        IOrderTicketAuditSink? auditSink = null)
    {
        _connection = connection;
        Db = db;
        AuditSink = auditSink ?? new NullOrderTicketAuditSink();
    }

    public AppDbContext Db { get; }
    public IOrderTicketAuditSink AuditSink { get; }
    public CountingTimeProvider TimeProvider { get; } = new(FixedUtcNow);
    public long UserId { get; private init; }
    public long OrderId { get; private init; }
    public IReadOnlyList<long> OrderItemIds { get; private init; } = [];

    public static async Task<RefundTestData> CreateAsync()
    {
        var (connection, db) = await CreateDatabaseAsync();
        return new RefundTestData(connection, db);
    }

    public static async Task<RefundTestData> CreateIssuedOrderAsync(
        decimal totalAmount = 210m,
        decimal discountAmount = 0m,
        decimal? payAmount = null,
        IReadOnlyList<decimal>? itemPrices = null,
        IOrderTicketAuditSink? auditSink = null)
    {
        var (connection, db) = await CreateDatabaseAsync();
        var prices = itemPrices ?? [105m, 105m];
        var userId = 7L;
        var orderId = 11L;
        var sessionId = 21L;
        var orderItemIds = prices
            .Select((_, index) => 101L + index)
            .ToArray();
        var fixture = new RefundTestData(connection, db, auditSink)
        {
            UserId = userId,
            OrderId = orderId,
            OrderItemIds = orderItemIds,
        };

        fixture.Db.Add(new ShowSession
        {
            SessionId = sessionId,
            ShowId = 90,
            SeatMapId = 30,
            StartTime = FixedUtcNow.AddDays(3),
            EndTime = FixedUtcNow.AddDays(3).AddHours(2),
            SaleStartTime = FixedUtcNow.AddMonths(-1),
            SaleEndTime = FixedUtcNow.AddDays(2),
            SessionStatus = "ONSALE",
        });
        fixture.Db.Add(new Order
        {
            OrderId = orderId,
            OrderNo = "ORD000011",
            UserId = userId,
            SessionId = sessionId,
            TotalAmount = totalAmount,
            DiscountAmount = discountAmount,
            TicketCount = prices.Count,
            OrderStatus = "ISSUED",
            ExpireTime = FixedUtcNow.AddHours(-1),
            PayTime = FixedUtcNow.AddHours(-2),
            IssueTime = FixedUtcNow.AddHours(-1),
            Source = "WEB",
        });
        fixture.Db.Add(new Payment
        {
            PaymentId = 31,
            PaymentNo = "PAY000031",
            OrderId = orderId,
            UserId = userId,
            PayAmount = payAmount ?? totalAmount - discountAmount,
            PayChannel = "ALIPAY",
            PayStatus = "SUCCESS",
            PayTime = FixedUtcNow.AddHours(-2),
        });

        for (var index = 0; index < prices.Count; index++)
        {
            var itemId = orderItemIds[index];
            fixture.Db.Add(new OrderItem
            {
                OrderItemId = itemId,
                OrderId = orderId,
                SeatId = 501 + index,
                PriceStrategyId = 601,
                UnitPrice = prices[index],
                ItemStatus = "NORMAL",
            });
            fixture.Db.Add(new ETicket
            {
                ETicketId = 201 + index,
                ETicketNo = $"TKT{201 + index:000000}",
                OrderItemId = itemId,
                UserId = userId,
                QrCode = $"qr-{201 + index}",
                AntiFakeCode = $"anti-{201 + index}",
                TicketStatus = "UNUSED",
            });
            fixture.Db.Add(new SeatReservation
            {
                SeatReservationId = 301 + index,
                SessionId = sessionId,
                SeatId = 501 + index,
                OrderItemId = itemId,
                ReservationType = "ORDER",
                ReservationStatus = "ACTIVE",
                ReserveTime = FixedUtcNow.AddHours(-3),
            });
        }

        await fixture.Db.SaveChangesAsync();
        return fixture;
    }

    private static async Task<(SqliteConnection Connection, AppDbContext Db)>
        CreateDatabaseAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SqliteAuthDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new SqliteAuthDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
        return (connection, db);
    }

    public RefundApplicationService CreateApplicationService() => new(
        Db,
        new RefundPolicyEngine(),
        TimeProvider);

    public static string CreateToken(long userId, string userName, string role)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthTestFactory.TestKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            AuthTestFactory.TestIssuer,
            AuthTestFactory.TestAudience,
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.UniqueName, userName),
                new Claim("role", role),
            ],
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    internal sealed class CountingTimeProvider(DateTime utcNow) : TimeProvider
    {
        public int GetUtcNowCallCount { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            GetUtcNowCallCount++;
            return new DateTimeOffset(utcNow);
        }
    }
}
