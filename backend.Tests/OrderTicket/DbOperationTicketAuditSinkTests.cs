using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Data;
using ShowtimeBackend.Services.OrderTicket;

namespace ShowtimeBackend.Tests.OrderTicket;

/// <summary>
/// DbOperationTicketAuditSink 落库集成测试：使用 SQLite 内存库验证
/// 领域事件确实写入 OPERATION_LOG 且字段保真。
/// </summary>
public sealed class DbOperationTicketAuditSinkTests
{
    [Fact]
    public async Task WriteAsync_PersistsFullAuditEventToOperationLog()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = CreateOptions(connection);

        await EnsureCreatedAsync(options);

        var sink = new DbOperationTicketAuditSink(new TestDbFactory(options));
        var auditEvent = new OrderTicketAuditEvent(
            "REFUND_APPROVED",
            OrderId: 42,
            Actor: "alice",
            TicketCount: 2,
            OccurredAt: new DateTime(2026, 8, 1, 10, 30, 0, DateTimeKind.Utc),
            RefundId: 7,
            ActualRefund: 12.34m,
            Metadata: new Dictionary<string, string>
            {
                ["ApproveStatus"] = "APPROVED",
                ["RefundStatus"] = "REFUND_PAID",
            });

        await sink.WriteAsync(auditEvent, CancellationToken.None);

        await using var verify = new AppDbContext(options);
        var row = await verify.OperationLogs.AsNoTracking().SingleAsync();

        Assert.Equal(DbOperationTicketAuditSink.OperationModule, row.OperationModule);
        Assert.Equal("REFUND_APPROVED", row.OperationType);
        Assert.Equal("alice", row.UserName);
        Assert.Equal("alice", row.CreateBy);
        Assert.True(row.Status);
        // 事件快照 JSON 必须保真：OrderId/RefundId/ActualRefund/Metadata 均可在列内还原
        Assert.Contains("\"orderId\":42", row.RequestParams);
        Assert.Contains("\"refundId\":7", row.RequestParams);
        Assert.Contains("\"actualRefund\":12.34", row.RequestParams);
        Assert.Contains("2026-08-01T10:30:00", row.RequestParams);
        // 字典 key 不参与命名策略，保持原样
        Assert.Contains("\"ApproveStatus\":\"APPROVED\"", row.RequestParams);
    }

    [Fact]
    public async Task WriteAsync_AppendsDistinctRowsForEachEvent()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = CreateOptions(connection);

        await EnsureCreatedAsync(options);

        var sink = new DbOperationTicketAuditSink(new TestDbFactory(options));
        await sink.WriteAsync(
            new OrderTicketAuditEvent(
                "PAYMENT_TICKET_ISSUED",
                OrderId: 1,
                Actor: "bob",
                TicketCount: 3,
                OccurredAt: DateTime.UtcNow),
            CancellationToken.None);
        await sink.WriteAsync(
            new OrderTicketAuditEvent(
                "TICKET_REDEEMED",
                OrderId: 2,
                Actor: "carol",
                TicketCount: 1,
                OccurredAt: DateTime.UtcNow),
            CancellationToken.None);

        await using var verify = new AppDbContext(options);
        var rows = await verify.OperationLogs.AsNoTracking().OrderBy(item => item.LogId).ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.Equal("PAYMENT_TICKET_ISSUED", rows[0].OperationType);
        Assert.Equal("TICKET_REDEEMED", rows[1].OperationType);
        Assert.NotEqual(rows[0].LogId, rows[1].LogId);
    }

    [Fact]
    public async Task WriteAsync_WritesEventWithNoRefundMetadata()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = CreateOptions(connection);

        await EnsureCreatedAsync(options);

        var sink = new DbOperationTicketAuditSink(new TestDbFactory(options));
        await sink.WriteAsync(
            new OrderTicketAuditEvent(
                "ADMIN_TICKET_ISSUED",
                OrderId: 9,
                Actor: "admin",
                TicketCount: 1,
                OccurredAt: DateTime.UtcNow),
            CancellationToken.None);

        await using var verify = new AppDbContext(options);
        var row = await verify.OperationLogs.AsNoTracking().SingleAsync();

        Assert.Equal("ADMIN_TICKET_ISSUED", row.OperationType);
        Assert.Contains("\"orderId\":9", row.RequestParams);
        // 无退款单号的事件不应携带具体退款值（null 会被序列化，仅验证无具体单号）
        Assert.DoesNotContain("\"refundId\":7", row.RequestParams);
    }

    private static DbContextOptions<AppDbContext> CreateOptions(SqliteConnection connection) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

    /// <summary>最小测试工厂：每次 WriteAsync 产出独立 AppDbContext 实例（与生产语义一致）。</summary>
    private sealed class TestDbFactory(DbContextOptions<AppDbContext> options)
        : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);

        public Task<AppDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private static async Task EnsureCreatedAsync(DbContextOptions<AppDbContext> options)
    {
        // OPERATION_LOG 的 Oracle 类型（NUMBER(19) PK/CLOB）无法直接由 EF 在 SQLite 建库
        // （SQLite 自增键必须是 INTEGER PRIMARY KEY），这里手工建等价表，
        // 列与 OperationLogConfiguration 一一对应。
        await using var db = new AppDbContext(options);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS OPERATION_LOG (
                LOG_ID            INTEGER PRIMARY KEY AUTOINCREMENT,
                USER_ID           INTEGER,
                USER_NAME         TEXT,
                SHOW_ID           INTEGER,
                OPERATION_MODULE  TEXT NOT NULL,
                OPERATION_TYPE    TEXT NOT NULL,
                REQUEST_URL       TEXT,
                REQUEST_PARAMS    TEXT,
                RESPONSE_RESULT   TEXT,
                IP_ADDRESS        TEXT,
                USER_AGENT        TEXT,
                COST_TIME         INTEGER,
                STATUS            INTEGER NOT NULL DEFAULT 1,
                ERROR_MSG         TEXT,
                CREATE_TIME       TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UPDATE_TIME       TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                CREATE_BY         TEXT,
                UPDATE_BY         TEXT
            );
            """);
    }
}