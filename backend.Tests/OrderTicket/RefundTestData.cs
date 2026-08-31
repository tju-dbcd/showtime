using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.OrderTicket;
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
    public long RefundId { get; private set; }
    public IReadOnlyList<long> RefundIds { get; private init; } = [];
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
        IOrderTicketAuditSink? auditSink = null,
        IReadOnlyList<long>? refundIds = null)
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
            RefundIds = refundIds ?? [],
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

    public static async Task<RefundTestData> CreateLegacyRefundAsync(long? appliedPolicyId)
    {
        var (connection, db) = await CreateDatabaseAsync();
        const long userId = 7;
        const long orderId = 11;
        const long sessionId = 21;
        long[] orderItemIds = [101, 102];
        const long refundId = 401;
        var fixture = new RefundTestData(connection, db)
        {
            UserId = userId,
            OrderId = orderId,
            RefundId = refundId,
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
            TotalAmount = 210m,
            TicketCount = 2,
            OrderStatus = "PART_REFUND",
            ExpireTime = FixedUtcNow.AddHours(-1),
            PayTime = FixedUtcNow.AddHours(-2),
            IssueTime = FixedUtcNow.AddHours(-1),
            Source = "WEB",
        });
        fixture.Db.Add(new OrderItem
        {
            OrderItemId = orderItemIds[0],
            OrderId = orderId,
            SeatId = 501,
            PriceStrategyId = 601,
            UnitPrice = 105m,
            ItemStatus = "REFUNDING",
        });
        fixture.Db.Add(new ETicket
        {
            ETicketId = 201,
            ETicketNo = "TKT000201",
            OrderItemId = orderItemIds[0],
            UserId = userId,
            QrCode = "qr-201",
            AntiFakeCode = "anti-201",
            TicketStatus = "REFUNDING",
        });
        fixture.Db.Add(new OrderItem
        {
            OrderItemId = orderItemIds[1],
            OrderId = orderId,
            SeatId = 502,
            PriceStrategyId = 601,
            UnitPrice = 105m,
            ItemStatus = "REFUNDING",
        });
        fixture.Db.Add(new ETicket
        {
            ETicketId = 202,
            ETicketNo = "TKT000202",
            OrderItemId = orderItemIds[1],
            UserId = userId,
            QrCode = "qr-202",
            AntiFakeCode = "anti-202",
            TicketStatus = "REFUNDING",
        });
        fixture.Db.Add(new RefundRequest
        {
            RefundId = refundId,
            RefundNo = "REF000401",
            OrderId = orderId,
            UserId = userId,
            RefundType = "FULL",
            RefundReason = "历史申请",
            RefundAmount = 210m,
            ActualRefund = 168m,
            FeeRate = 0.8m,
            AppliedPolicyId = appliedPolicyId,
            AppliedServiceFee = 0m,
            ApproveStatus = "PENDING",
            RefundStatus = "PENDING",
            CreateTime = FixedUtcNow.AddHours(-1),
            CreateBy = "seed",
            UpdateBy = "seed",
            Items =
            [
                new RefundItem
                {
                    RefundItemId = 501,
                    OrderItemId = orderItemIds[1],
                    RefundBaseAmount = 105m,
                    CreateBy = "seed",
                    UpdateBy = "seed",
                },
                new RefundItem
                {
                    RefundItemId = 502,
                    OrderItemId = orderItemIds[0],
                    RefundBaseAmount = 105m,
                    CreateBy = "seed",
                    UpdateBy = "seed",
                },
            ],
        });

        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        return fixture;
    }

    public static async Task<RefundTestData> CreatePendingRefundAsync(
        int itemCount = 1,
        bool reverseRefundItemSeed = false)
    {
        var fixture = await CreateIssuedOrderAsync(
            itemPrices: Enumerable.Repeat(105m, itemCount).ToArray());
        fixture.RefundId = 401;

        var orderItems = await fixture.Db.Set<OrderItem>()
            .Include(item => item.ETicket)
            .OrderBy(item => item.OrderItemId)
            .ToListAsync();
        foreach (var orderItem in orderItems)
        {
            orderItem.ItemStatus = "REFUNDING";
            orderItem.ETicket!.TicketStatus = "REFUNDING";
        }

        fixture.Db.Add(new RefundRequest
        {
            RefundId = fixture.RefundId,
            RefundNo = "REF000401",
            OrderId = fixture.OrderId,
            UserId = fixture.UserId,
            RefundType = "FULL",
            RefundReason = "行程变更",
            RefundAmount = itemCount * 105m,
            ActualRefund = itemCount * 84m,
            FeeRate = 0.8m,
            AppliedServiceFee = 0m,
            ApproveStatus = "PENDING",
            RefundStatus = "PENDING",
            CreateTime = FixedUtcNow.AddHours(-1),
            CreateBy = "alice",
            UpdateBy = "alice",
            Items = (reverseRefundItemSeed ? orderItems.AsEnumerable().Reverse() : orderItems)
                .Select((item, index) => new RefundItem
                {
                    RefundItemId = 501 + index,
                    OrderItemId = item.OrderItemId,
                    RefundBaseAmount = item.UnitPrice,
                    CreateBy = "alice",
                    UpdateBy = "alice",
                })
                .ToList(),
        });

        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        return fixture;
    }

    public static async Task<RefundTestData> CreateTwoPendingRefundsAsync()
    {
        var fixture = await CreateIssuedOrderAsync(refundIds: [401, 402]);
        fixture.RefundId = 401;

        var orderItems = await fixture.Db.Set<OrderItem>()
            .Include(item => item.ETicket)
            .OrderBy(item => item.OrderItemId)
            .ToListAsync();
        foreach (var orderItem in orderItems)
        {
            orderItem.ItemStatus = "REFUNDING";
            orderItem.ETicket!.TicketStatus = "REFUNDING";
        }

        for (var index = 0; index < orderItems.Count; index++)
        {
            var orderItem = orderItems[index];
            fixture.Db.Add(new RefundRequest
            {
                RefundId = fixture.RefundIds[index],
                RefundNo = $"REF{fixture.RefundIds[index]:000000}",
                OrderId = fixture.OrderId,
                UserId = fixture.UserId,
                RefundType = "PART",
                RefundReason = $"分项退票 {index + 1}",
                RefundAmount = orderItem.UnitPrice,
                ActualRefund = 84m,
                FeeRate = 0.8m,
                AppliedServiceFee = 0m,
                ApproveStatus = "PENDING",
                RefundStatus = "PENDING",
                CreateTime = FixedUtcNow.AddHours(-1),
                CreateBy = "alice",
                UpdateBy = "alice",
                Items =
                [
                    new RefundItem
                    {
                        RefundItemId = 501 + index,
                        OrderItemId = orderItem.OrderItemId,
                        RefundBaseAmount = orderItem.UnitPrice,
                        CreateBy = "alice",
                        UpdateBy = "alice",
                    },
                ],
            });
        }

        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        return fixture;
    }

    public static async Task<SharedRefundDatabase> CreateSharedSqliteAsync()
    {
        var database = await SharedRefundDatabase.CreateAsync();
        try
        {
            await using var db = database.CreateContext();
            await db.Database.OpenConnectionAsync();
            await db.Database.EnsureCreatedAsync();
            await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");

            db.Add(new ShowSession
            {
                SessionId = 21,
                ShowId = 90,
                SeatMapId = 30,
                StartTime = FixedUtcNow.AddDays(3),
                EndTime = FixedUtcNow.AddDays(3).AddHours(2),
                SaleStartTime = FixedUtcNow.AddMonths(-1),
                SaleEndTime = FixedUtcNow.AddDays(2),
                SessionStatus = "ONSALE",
            });
            db.Add(new Order
            {
                OrderId = 11,
                OrderNo = "ORD000011",
                UserId = 7,
                SessionId = 21,
                TotalAmount = 105m,
                TicketCount = 1,
                OrderStatus = "ISSUED",
                ExpireTime = FixedUtcNow.AddHours(-1),
                PayTime = FixedUtcNow.AddHours(-2),
                IssueTime = FixedUtcNow.AddHours(-1),
                Source = "WEB",
            });
            db.Add(new OrderItem
            {
                OrderItemId = 101,
                OrderId = 11,
                SeatId = 501,
                PriceStrategyId = 601,
                UnitPrice = 105m,
                ItemStatus = "REFUNDING",
            });
            db.Add(new ETicket
            {
                ETicketId = 201,
                ETicketNo = "TKT000201",
                OrderItemId = 101,
                UserId = 7,
                QrCode = "qr-201",
                AntiFakeCode = "anti-201",
                TicketStatus = "REFUNDING",
            });
            db.Add(new RefundRequest
            {
                RefundId = 401,
                RefundNo = "REF000401",
                OrderId = 11,
                UserId = 7,
                RefundType = "FULL",
                RefundReason = "并发测试",
                RefundAmount = 105m,
                ActualRefund = 84m,
                FeeRate = 0.8m,
                AppliedServiceFee = 0m,
                ApproveStatus = "PENDING",
                RefundStatus = "PENDING",
                CreateTime = FixedUtcNow.AddHours(-1),
                CreateBy = "alice",
                UpdateBy = "alice",
                Items =
                [
                    new RefundItem
                    {
                        RefundItemId = 501,
                        OrderItemId = 101,
                        RefundBaseAmount = 105m,
                        CreateBy = "alice",
                        UpdateBy = "alice",
                    },
                ],
            });
            await db.SaveChangesAsync();
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    public static async Task SeedIssuedOrderAsync(AuthTestFactory factory)
    {
        await factory.ResetDatabaseAsync();
        await factory.ExecuteDbContextAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
            var now = factory.UtcNow.UtcDateTime;
            db.Add(new ShowSession
            {
                SessionId = 20,
                ShowId = 90,
                SeatMapId = 30,
                StartTime = now.AddDays(3),
                EndTime = now.AddDays(3).AddHours(2),
                SaleStartTime = now.AddMonths(-1),
                SaleEndTime = now.AddDays(2),
                SessionStatus = "ONSALE",
            });
            db.Add(new Order
            {
                OrderId = 10,
                OrderNo = "ORD000010",
                UserId = 7,
                SessionId = 20,
                TotalAmount = 105m,
                TicketCount = 1,
                OrderStatus = "ISSUED",
                ExpireTime = now.AddHours(-1),
                PayTime = now.AddHours(-2),
                IssueTime = now.AddHours(-1),
                Source = "WEB",
            });
            db.Add(new Payment
            {
                PaymentId = 30,
                PaymentNo = "PAY000030",
                OrderId = 10,
                UserId = 7,
                PayAmount = 105m,
                PayChannel = "ALIPAY",
                PayStatus = "SUCCESS",
                PayTime = now.AddHours(-2),
            });
            db.Add(new OrderItem
            {
                OrderItemId = 1,
                OrderId = 10,
                SeatId = 501,
                PriceStrategyId = 601,
                UnitPrice = 105m,
                ItemStatus = "NORMAL",
            });
            db.Add(new ETicket
            {
                ETicketId = 201,
                ETicketNo = "TKT000201",
                OrderItemId = 1,
                UserId = 7,
                QrCode = "qr-201",
                AntiFakeCode = "anti-201",
                TicketStatus = "UNUSED",
            });
            db.Add(new SeatReservation
            {
                SeatReservationId = 301,
                SessionId = 20,
                SeatId = 501,
                OrderItemId = 1,
                ReservationType = "ORDER",
                ReservationStatus = "ACTIVE",
                ReserveTime = now.AddHours(-3),
            });
            db.Add(new RefundPolicy
            {
                PolicyId = 801,
                PolicyName = "全局策略",
                RefundDeadlineHour = 24,
                RefundRate = 0.8m,
                ServiceFee = 0m,
                Priority = 1,
                Status = 1,
                CreateBy = "seed",
                UpdateBy = "seed",
            });
            await db.SaveChangesAsync();
            return true;
        });
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

    public RefundReviewService CreateReviewService() => new(
        Db,
        TimeProvider,
        new TestRefundLockCoordinator(Db),
        NullLogger<RefundReviewService>.Instance,
        AuditSink);

    public AppDbContext CreateDbContext(params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<SqliteAuthDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(interceptors)
            .Options;
        return new SqliteAuthDbContext(options);
    }

    public void BackupTo(SqliteConnection destination) =>
        _connection.BackupDatabase(destination);

    public async Task<OrderTicketResult<RefundResponse>> ApproveWithFreshContextAsync(
        long refundId)
    {
        await using var db = CreateDbContext();
        var service = new RefundReviewService(
            db,
            TimeProvider,
            new TestRefundLockCoordinator(db),
            NullLogger<RefundReviewService>.Instance,
            AuditSink);
        return await service.ApproveAsync(
            "admin",
            refundId,
            new ApproveRefundRequest(null),
            CancellationToken.None);
    }

    public async Task MarkRefundReviewedAsync(string approveStatus, string refundStatus)
    {
        var refund = await Db.Set<RefundRequest>()
            .SingleAsync(item => item.RefundId == RefundId);
        refund.ApproveStatus = approveStatus;
        refund.RefundStatus = refundStatus;
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
    }

    public Task<string> ItemStatusAsync() => Db.Set<OrderItem>()
        .AsNoTracking()
        .Where(item => item.OrderItemId == OrderItemIds[0])
        .Select(item => item.ItemStatus)
        .SingleAsync();

    public Task<string> TicketStatusAsync() => Db.Set<ETicket>()
        .AsNoTracking()
        .Where(item => item.OrderItemId == OrderItemIds[0])
        .Select(item => item.TicketStatus)
        .SingleAsync();

    public Task<string> ReservationStatusAsync() => Db.Set<SeatReservation>()
        .AsNoTracking()
        .Where(item => item.OrderItemId == OrderItemIds[0])
        .Select(item => item.ReservationStatus)
        .SingleAsync();

    public Task<decimal> PaymentRefundAmountAsync() => Db.Set<Payment>()
        .AsNoTracking()
        .Where(item => item.OrderId == OrderId && item.PayStatus == "SUCCESS")
        .Select(item => item.RefundAmount)
        .SingleAsync();

    public Task<string> OrderStatusAsync() => Db.Set<Order>()
        .AsNoTracking()
        .Where(item => item.OrderId == OrderId)
        .Select(item => item.OrderStatus)
        .SingleAsync();

    public Task<string> RefundApproveStatusAsync() => Db.Set<RefundRequest>()
        .AsNoTracking()
        .Where(item => item.RefundId == RefundId)
        .Select(item => item.ApproveStatus)
        .SingleAsync();

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

internal sealed class SharedRefundDatabase : IAsyncDisposable
{
    private readonly string _connectionString;
    private readonly SqliteConnection _anchor;

    private SharedRefundDatabase(string connectionString, SqliteConnection anchor)
    {
        _connectionString = connectionString;
        _anchor = anchor;
    }

    public static async Task<SharedRefundDatabase> CreateAsync()
    {
        var name = $"refund-{Guid.NewGuid():N}";
        var connectionString = $"Data Source={name};Mode=Memory;Cache=Shared";
        var anchor = new SqliteConnection(connectionString);
        await anchor.OpenAsync();
        return new SharedRefundDatabase(connectionString, anchor);
    }

    public SqliteAuthDbContext CreateContext() => new(
        new DbContextOptionsBuilder<SqliteAuthDbContext>()
            .UseSqlite(_connectionString)
            .Options);

    public ValueTask DisposeAsync() => _anchor.DisposeAsync();
}
