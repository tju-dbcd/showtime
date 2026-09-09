using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using ShowtimeBackend.Data;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Services.OrderTicket;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class OrderIdempotencyModelTests
{
    [Fact]
    public void OrderConfiguration_MapsIdempotencyColumnsConstraintAndUniqueIndex()
    {
        using var db = CreateDbContext();
        var entityType = db.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(Order))!;

        var key = entityType.FindProperty(nameof(Order.IdempotencyKey))!;
        Assert.True(key.IsNullable);
        Assert.Equal("IDEMPOTENCY_KEY", key.GetColumnName());
        Assert.Equal(
            "VARCHAR2(64 CHAR)",
            key.FindAnnotation(RelationalAnnotationNames.ColumnType)?.Value);
        Assert.Equal(64, key.GetMaxLength());

        var hash = entityType.FindProperty(nameof(Order.IdempotencyRequestHash))!;
        Assert.True(hash.IsNullable);
        Assert.Equal("IDEMPOTENCY_REQUEST_HASH", hash.GetColumnName());
        Assert.Equal(
            "CHAR(64 CHAR)",
            hash.FindAnnotation(RelationalAnnotationNames.ColumnType)?.Value);
        Assert.Equal(64, hash.GetMaxLength());
        Assert.True(hash.IsFixedLength());

        var index = entityType.GetIndexes().Single(candidate =>
            candidate.GetDatabaseName() == "UK_T_ORDER_USER_IDEMPOTENCY");
        Assert.True(index.IsUnique);
        Assert.Equal(
            [nameof(Order.UserId), nameof(Order.IdempotencyKey)],
            index.Properties.Select(property => property.Name));

        var pair = entityType.GetCheckConstraints().Single(constraint =>
            constraint.Name == "CHK_T_ORDER_IDEMPOTENCY_PAIR");
        Assert.Contains("IDEMPOTENCY_KEY IS NULL", pair.Sql, StringComparison.Ordinal);
        Assert.Contains("IDEMPOTENCY_REQUEST_HASH IS NOT NULL", pair.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_IsRepeatableAndDeclaresExactOracleObjects()
    {
        var script = File.ReadAllText(Path.Combine(
            FindSolutionRoot(),
            "db",
            "migrations",
            "20260904__order_idempotency.sql"));

        Assert.Contains("IDEMPOTENCY_KEY VARCHAR2(64 CHAR)", script, StringComparison.Ordinal);
        Assert.Contains("IDEMPOTENCY_REQUEST_HASH CHAR(64 CHAR)", script, StringComparison.Ordinal);
        Assert.Contains("CHK_T_ORDER_IDEMPOTENCY_PAIR", script, StringComparison.Ordinal);
        Assert.Contains("UK_T_ORDER_USER_IDEMPOTENCY", script, StringComparison.Ordinal);
        Assert.Contains(
            "CREATE UNIQUE INDEX UK_T_ORDER_USER_IDEMPOTENCY",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "CASE WHEN IDEMPOTENCY_KEY IS NOT NULL THEN USER_ID END",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "CASE WHEN IDEMPOTENCY_KEY IS NOT NULL THEN IDEMPOTENCY_KEY END",
            script,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "UNIQUE (USER_ID, IDEMPOTENCY_KEY)",
            script,
            StringComparison.Ordinal);
        Assert.Contains("IF v_count = 0 THEN", script, StringComparison.Ordinal);
        Assert.Contains("ALL_TAB_COLUMNS", script, StringComparison.Ordinal);
        Assert.Contains("ALL_CONSTRAINTS", script, StringComparison.Ordinal);
        Assert.Contains("ALL_INDEXES", script, StringComparison.Ordinal);
        Assert.Contains("ALL_IND_EXPRESSIONS", script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "ORA-00001: unique constraint (APP_OWNER.UK_T_ORDER_USER_IDEMPOTENCY) violated",
        1)]
    [InlineData(
        "SQLite Error 19: UNIQUE constraint failed: T_ORDER.USER_ID, T_ORDER.IDEMPOTENCY_KEY",
        1)]
    [InlineData(
        "ORA-00001: unique constraint (APP_OWNER.UK_SEAT_RESERVATION_ORDER_ITEM) violated",
        2)]
    [InlineData(
        "SQLite Error 19: UNIQUE constraint failed: SEAT_RESERVATION.ORDER_ITEM_ID",
        2)]
    [InlineData(
        "ORA-00001: unique constraint (APP_OWNER.UK_T_ORDER_NO) violated",
        3)]
    [InlineData("not a unique failure", 0)]
    public void ConstraintClassifier_OnlyRecognizesExactIdempotencyConstraint(
        string message,
        int expected)
    {
        var exception = new DbUpdateException(
            "outer",
            new InvalidOperationException(message));

        Assert.Equal(
            (OrderCreateUniqueConstraint)expected,
            OrderCreateConstraintClassifier.Classify(exception));
    }

    [Fact]
    public void RequestHash_IsOrderInsensitiveAndFieldSensitive()
    {
        var first = OrderIdempotencyRequestHasher.Compute(
            10,
            [new(51, 61, null, "lock-51"), new(50, 60, 70, "lock-50")],
            "remark");
        var reordered = OrderIdempotencyRequestHasher.Compute(
            10,
            [new(50, 60, 70, "lock-50"), new(51, 61, null, "lock-51")],
            "remark");
        var changed = OrderIdempotencyRequestHasher.Compute(
            10,
            [new(50, 60, 70, "lock-50"), new(51, 62, null, "lock-51")],
            "remark");
        var whitespaceToken = OrderIdempotencyRequestHasher.Compute(
            10,
            [new(50, 60, 70, " lock-50 "), new(51, 61, null, "lock-51")],
            "remark");

        Assert.Equal(first, reordered);
        Assert.NotEqual(first, changed);
        Assert.NotEqual(first, whitespaceToken);
        Assert.Matches("^[0-9A-F]{64}$", first);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Showtime.sln")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not find the Showtime solution root.");
    }
}
