using System.Text.RegularExpressions;
using Oracle.ManagedDataAccess.Client;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class OracleOrderIdempotencyMigrationSafetyTests
{
    private const string CheckConstraint = "CHK_T_ORDER_IDEMPOTENCY_PAIR";
    private const string UniqueConstraint = "UK_T_ORDER_USER_IDEMPOTENCY";

    [OracleOrderIdempotencyFact]
    public async Task OracleMigration_IsRepeatableAndRepairsDroppedConstraints()
    {
        using var oracleGate = await OracleOrderTicketGate.EnterAsync();
        await using var connection = await OpenValidatedConnectionAsync();

        try
        {
            await ExecuteMigrationAsync(connection);
            await ExecuteMigrationAsync(connection);
            await AssertTerminalStateAsync(connection);

            await ExecuteAsync(
                connection,
                $"ALTER TABLE T_ORDER DROP CONSTRAINT {CheckConstraint}");
            await ExecuteAsync(
                connection,
                $"ALTER TABLE T_ORDER DROP CONSTRAINT {UniqueConstraint}");
            Assert.Equal(
                0m,
                await ScalarAsync<decimal>(
                    connection,
                    "SELECT COUNT(*) FROM USER_CONSTRAINTS " +
                    "WHERE TABLE_NAME = 'T_ORDER' AND CONSTRAINT_NAME IN " +
                    $"('{CheckConstraint}', '{UniqueConstraint}')"));

            await ExecuteMigrationAsync(connection);
            await AssertTerminalStateAsync(connection);
        }
        finally
        {
            await ExecuteMigrationAsync(connection);
            await AssertTerminalStateAsync(connection);
        }
    }

    private static async Task<OracleConnection> OpenValidatedConnectionAsync()
    {
        var raw = Environment.GetEnvironmentVariable(
            "SHOWTIME_ORACLE_IDEMPOTENCY_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException(
                "SHOWTIME_RUN_ORACLE_IDEMPOTENCY_TESTS=1 requires " +
                "SHOWTIME_ORACLE_IDEMPOTENCY_TEST_CONNECTION.");
        }

        var builder = new OracleConnectionStringBuilder(raw)
        {
            Pooling = false,
            ConnectionTimeout = 20,
        };
        var configuredSchema = ValidatePersonalSchema(builder.UserID);
        var connection = new OracleConnection(builder.ConnectionString);
        await connection.OpenAsync().WaitAsync(TimeSpan.FromSeconds(25));

        var sessionUser = ValidatePersonalSchema(await ScalarAsync<string>(
            connection,
            "SELECT SYS_CONTEXT('USERENV', 'SESSION_USER') FROM DUAL"));
        var currentSchema = ValidatePersonalSchema(await ScalarAsync<string>(
            connection,
            "SELECT SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA') FROM DUAL"));
        if (!configuredSchema.Equals(sessionUser, StringComparison.OrdinalIgnoreCase) ||
            !configuredSchema.Equals(currentSchema, StringComparison.OrdinalIgnoreCase))
        {
            await connection.DisposeAsync();
            throw new InvalidOperationException(
                "Oracle idempotency tests must log in as and remain in the " +
                "configured personal schema.");
        }

        var tableCount = await ScalarAsync<decimal>(
            connection,
            "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME = 'T_ORDER'");
        if (tableCount != 1m)
        {
            await connection.DisposeAsync();
            throw new InvalidOperationException(
                "Oracle idempotency tests require a personal-schema T_ORDER table; " +
                "synonyms and shared-owner tables are refused.");
        }

        return connection;
    }

    private static string ValidatePersonalSchema(string? value)
    {
        var schema = value?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(schema) ||
            schema is "APP_OWNER" or "DEPLOY_USER" ||
            !Regex.IsMatch(schema, "^[A-Z][A-Z0-9_$#]*$"))
        {
            throw new InvalidOperationException(
                "Oracle idempotency tests require an unquoted personal schema account.");
        }

        return schema;
    }

    private static async Task ExecuteMigrationAsync(OracleConnection connection)
    {
        var migrationPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../db/migrations/20260904__order_idempotency.sql"));
        var lines = await File.ReadAllLinesAsync(migrationPath);
        var block = new List<string>();
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("SET ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("WHENEVER ", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (trimmed == "/")
            {
                var sql = string.Join(Environment.NewLine, block).Trim();
                if (sql.Length > 0)
                    await ExecuteAsync(connection, sql);
                block.Clear();
                continue;
            }

            block.Add(line);
        }

        var trailingSql = string.Join(Environment.NewLine, block).Trim();
        if (trailingSql.Length > 0 &&
            !trailingSql.StartsWith(
                "SELECT SYS_CONTEXT('USERENV', 'SESSION_USER')",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The order idempotency migration contains an unterminated SQL*Plus block.");
        }
    }

    private static async Task AssertTerminalStateAsync(OracleConnection connection)
    {
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT COLUMN_NAME, DATA_TYPE, CHAR_LENGTH, CHAR_USED, NULLABLE " +
                "FROM USER_TAB_COLUMNS WHERE TABLE_NAME = 'T_ORDER' " +
                "AND COLUMN_NAME IN ('IDEMPOTENCY_KEY', 'IDEMPOTENCY_REQUEST_HASH') " +
                "ORDER BY COLUMN_NAME";
            await using var reader = await command.ExecuteReaderAsync();
            var columns = new List<(string Name, string Type, int Length, string CharUsed, string Nullable)>();
            while (await reader.ReadAsync())
            {
                columns.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    Convert.ToInt32(reader.GetDecimal(2)),
                    reader.GetString(3),
                    reader.GetString(4)));
            }

            Assert.Equal(2, columns.Count);
            Assert.Contains(
                columns,
                column => column == ("IDEMPOTENCY_KEY", "VARCHAR2", 64, "C", "Y"));
            Assert.Contains(
                columns,
                column => column == ("IDEMPOTENCY_REQUEST_HASH", "CHAR", 64, "C", "Y"));
        }

        var checkDefinition = await ScalarAsync<string>(
            connection,
            "SELECT REGEXP_REPLACE(UPPER(REPLACE(SEARCH_CONDITION_VC, CHR(34), '')), " +
            "'[[:space:]]', '') FROM USER_CONSTRAINTS " +
            $"WHERE TABLE_NAME = 'T_ORDER' AND CONSTRAINT_NAME = '{CheckConstraint}' " +
            "AND CONSTRAINT_TYPE = 'C'");
        Assert.Equal(
            "(IDEMPOTENCY_KEYISNULLANDIDEMPOTENCY_REQUEST_HASHISNULL)OR" +
            "(IDEMPOTENCY_KEYISNOTNULLANDIDEMPOTENCY_REQUEST_HASHISNOTNULL)",
            checkDefinition);

        Assert.Equal(
            1m,
            await ScalarAsync<decimal>(
                connection,
                "SELECT COUNT(*) FROM USER_CONSTRAINTS " +
                $"WHERE TABLE_NAME = 'T_ORDER' AND CONSTRAINT_NAME = '{UniqueConstraint}' " +
                "AND CONSTRAINT_TYPE = 'U'"));

        await using var uniqueColumnsCommand = connection.CreateCommand();
        uniqueColumnsCommand.CommandText =
            "SELECT COLUMN_NAME FROM USER_CONS_COLUMNS " +
            $"WHERE TABLE_NAME = 'T_ORDER' AND CONSTRAINT_NAME = '{UniqueConstraint}' " +
            "ORDER BY POSITION";
        await using var uniqueColumnsReader = await uniqueColumnsCommand.ExecuteReaderAsync();
        var uniqueColumns = new List<string>();
        while (await uniqueColumnsReader.ReadAsync())
            uniqueColumns.Add(uniqueColumnsReader.GetString(0));
        Assert.Equal(["USER_ID", "IDEMPOTENCY_KEY"], uniqueColumns);
    }

    private static async Task ExecuteAsync(
        OracleConnection connection,
        string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(
        OracleConnection connection,
        string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        if (value is null or DBNull)
            throw new InvalidOperationException("Oracle scalar query returned null.");
        return (T)(Convert.ChangeType(value, typeof(T)) ??
            throw new InvalidOperationException("Oracle scalar conversion returned null."));
    }

    private sealed class OracleOrderIdempotencyFactAttribute : FactAttribute
    {
        public OracleOrderIdempotencyFactAttribute()
        {
            if (Environment.GetEnvironmentVariable(
                    "SHOWTIME_RUN_ORACLE_IDEMPOTENCY_TESTS") != "1")
            {
                Skip = "SHOWTIME_RUN_ORACLE_IDEMPOTENCY_TESTS is not 1; " +
                       "no Oracle idempotency connection will be opened.";
            }
        }
    }
}
