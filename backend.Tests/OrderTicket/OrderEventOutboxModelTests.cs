using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using ShowtimeBackend.Data;
using ShowtimeBackend.Entities.OrderTicket;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class OrderEventOutboxModelTests
{
    [Fact]
    public async Task SqliteSchemaEnforcesOutboxStatusConstraint()
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SqliteAuthDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new SqliteAuthDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var now = DateTime.UtcNow;
        db.OrderEventOutbox.Add(new OrderEventOutbox
        {
            EventId = Guid.NewGuid().ToString("D"),
            EventType = "OrderCreated.v1",
            RoutingKey = "order.created.v1",
            AggregateId = 1,
            UserId = 1,
            Payload = "{}",
            OccurredAt = now,
            Status = "UNKNOWN",
            NextAttemptAt = now,
            CreateTime = now,
            UpdateTime = now,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public void EfModelMatchesOracleOutboxContract()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        using var db = new AppDbContext(options);
        var entity = db.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(OrderEventOutbox))!;
        var table = StoreObjectIdentifier.Table("T_ORDER_EVENT_OUTBOX", "APP_OWNER");

        Assert.Equal("T_ORDER_EVENT_OUTBOX", entity.GetTableName());
        Assert.Equal("CHAR(36 CHAR)", entity.FindProperty(nameof(OrderEventOutbox.EventId))!.GetColumnType());
        Assert.Equal("CLOB", entity.FindProperty(nameof(OrderEventOutbox.Payload))!.GetColumnType());
        Assert.Equal("TIMESTAMP(6)", entity.FindProperty(nameof(OrderEventOutbox.NextAttemptAt))!.GetColumnType());
        Assert.Equal("PK_ORDER_EVENT_OUTBOX", entity.FindPrimaryKey()!.GetName());
        Assert.Contains(entity.GetCheckConstraints(), constraint =>
            constraint.Name == "CHK_ORDER_OUTBOX_STATUS" &&
            constraint.Sql!.Contains("'PUBLISHED'", StringComparison.Ordinal));
        var retry = entity.GetIndexes().Single(index => index.GetDatabaseName() == "IDX_ORDER_OUTBOX_RETRY");
        Assert.Equal(
            [nameof(OrderEventOutbox.Status), nameof(OrderEventOutbox.NextAttemptAt), nameof(OrderEventOutbox.EventId)],
            retry.Properties.Select(property => property.Name));
        Assert.Equal("EVENT_ID", entity.FindProperty(nameof(OrderEventOutbox.EventId))!.GetColumnName(table));
    }

    [Fact]
    public void OracleMigrationIsIdempotentFailClosedAndSafetyGuarded()
    {
        var migration = File.ReadAllText(MigrationPath());

        Assert.Contains("T_ORDER_EVENT_OUTBOX", migration);
        Assert.Contains("Unsupported migration owner", migration);
        Assert.Contains("APP_OWNER", migration);
        Assert.Contains("DEPLOY_USER", migration);
        Assert.Contains("ALL_TAB_COLUMNS", migration);
        Assert.Contains("CHK_ORDER_OUTBOX_STATUS", migration);
        Assert.Contains("IDX_ORDER_OUTBOX_RETRY", migration);
        Assert.Contains("IF v_count = 0 THEN", migration);
    }

    private static string MigrationPath() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "../../../../db/migrations/20260905__order_event_outbox.sql"));
}
