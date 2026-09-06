using System.Text.RegularExpressions;
using Oracle.ManagedDataAccess.Client;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class OracleOrderOutboxMigrationSafetyTests
{
    [OracleOrderOutboxFact]
    public async Task OracleMigration_IsRepeatableAndRepairsOwnedMetadata()
    {
        using var oracleGate = await OracleOrderTicketGate.EnterAsync();
        await using var connection = await OpenValidatedConnectionAsync();

        try
        {
            await ExecuteMigrationAsync(connection);
            await ExecuteMigrationAsync(connection);
            await AssertTerminalStateAsync(connection);

            await ExecuteAsync(connection,
                "ALTER TABLE T_ORDER_EVENT_OUTBOX DROP CONSTRAINT CHK_ORDER_OUTBOX_STATUS");
            await ExecuteAsync(connection, "DROP INDEX IDX_ORDER_OUTBOX_RETRY");
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
        var raw = Environment.GetEnvironmentVariable("SHOWTIME_ORACLE_OUTBOX_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException(
                "SHOWTIME_RUN_ORACLE_OUTBOX_TESTS=1 requires SHOWTIME_ORACLE_OUTBOX_TEST_CONNECTION.");
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
            connection, "SELECT SYS_CONTEXT('USERENV', 'SESSION_USER') FROM DUAL"));
        var currentSchema = ValidatePersonalSchema(await ScalarAsync<string>(
            connection, "SELECT SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA') FROM DUAL"));
        if (!configuredSchema.Equals(sessionUser, StringComparison.OrdinalIgnoreCase) ||
            !configuredSchema.Equals(currentSchema, StringComparison.OrdinalIgnoreCase))
        {
            await connection.DisposeAsync();
            throw new InvalidOperationException(
                "Oracle outbox tests must log in as and remain in the configured personal schema.");
        }

        var ownOrderTable = await ScalarAsync<decimal>(connection,
            "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME = 'T_ORDER'");
        if (ownOrderTable != 1m)
        {
            await connection.DisposeAsync();
            throw new InvalidOperationException(
                "Oracle outbox tests require a personal-schema T_ORDER table; shared objects are refused.");
        }

        return connection;
    }

    private static string ValidatePersonalSchema(string? value)
    {
        var schema = value?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(schema) || schema is "APP_OWNER" or "DEPLOY_USER" ||
            !Regex.IsMatch(schema, "^[A-Z][A-Z0-9_$#]*$"))
        {
            throw new InvalidOperationException(
                "Oracle outbox tests require an unquoted personal schema account.");
        }

        return schema;
    }

    private static async Task ExecuteMigrationAsync(OracleConnection connection)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../db/migrations/20260905__order_event_outbox.sql"));
        var block = new List<string>();
        foreach (var line in await File.ReadAllLinesAsync(path))
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
                {
                    await ExecuteAsync(connection, sql);
                }
                block.Clear();
            }
            else
            {
                block.Add(line);
            }
        }

        if (block.Any(line => !string.IsNullOrWhiteSpace(line)))
        {
            throw new InvalidOperationException("The outbox migration has an unterminated SQL*Plus block.");
        }
    }

    private static async Task AssertTerminalStateAsync(OracleConnection connection)
    {
        Assert.Equal(18m, await ScalarAsync<decimal>(connection,
            "SELECT COUNT(*) FROM USER_TAB_COLUMNS WHERE TABLE_NAME = 'T_ORDER_EVENT_OUTBOX'"));
        Assert.Equal(1m, await ScalarAsync<decimal>(connection,
            "SELECT COUNT(*) FROM USER_TAB_COLUMNS WHERE TABLE_NAME = 'T_ORDER_EVENT_OUTBOX' " +
            "AND COLUMN_NAME = 'EVENT_ID' AND DATA_TYPE = 'CHAR' AND CHAR_LENGTH = 36 " +
            "AND CHAR_USED = 'C' AND NULLABLE = 'N'"));
        Assert.Equal(1m, await ScalarAsync<decimal>(connection,
            "SELECT COUNT(*) FROM USER_TAB_COLUMNS WHERE TABLE_NAME = 'T_ORDER_EVENT_OUTBOX' " +
            "AND COLUMN_NAME = 'PAYLOAD' AND DATA_TYPE = 'CLOB' AND NULLABLE = 'N'"));
        Assert.Equal(1m, await ScalarAsync<decimal>(connection,
            "SELECT COUNT(*) FROM USER_CONSTRAINTS WHERE TABLE_NAME = 'T_ORDER_EVENT_OUTBOX' " +
            "AND CONSTRAINT_NAME = 'PK_ORDER_EVENT_OUTBOX' AND CONSTRAINT_TYPE = 'P'"));
        var check = await ScalarAsync<string>(connection,
            "SELECT REGEXP_REPLACE(UPPER(REPLACE(SEARCH_CONDITION_VC, CHR(34), '')), " +
            "'[[:space:]]', '') FROM USER_CONSTRAINTS WHERE TABLE_NAME = 'T_ORDER_EVENT_OUTBOX' " +
            "AND CONSTRAINT_NAME = 'CHK_ORDER_OUTBOX_STATUS' AND CONSTRAINT_TYPE = 'C'");
        Assert.Equal("STATUSIN('PENDING','PROCESSING','PUBLISHED','FAILED')", check);
        Assert.Equal(
            ["STATUS", "NEXT_ATTEMPT_AT", "EVENT_ID"],
            await ReadIndexColumnsAsync(connection, "IDX_ORDER_OUTBOX_RETRY"));
        Assert.Equal(
            ["AGGREGATE_ID", "EVENT_TYPE"],
            await ReadIndexColumnsAsync(connection, "IDX_ORDER_OUTBOX_AGGREGATE"));
    }

    private static async Task<List<string>> ReadIndexColumnsAsync(
        OracleConnection connection,
        string indexName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COLUMN_NAME FROM USER_IND_COLUMNS " +
            $"WHERE TABLE_NAME = 'T_ORDER_EVENT_OUTBOX' AND INDEX_NAME = '{indexName}' ORDER BY COLUMN_POSITION";
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new List<string>();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }
        return columns;
    }

    private static async Task ExecuteAsync(OracleConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(OracleConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        if (value is null or DBNull)
        {
            throw new InvalidOperationException("Oracle scalar query returned null.");
        }
        return (T)(Convert.ChangeType(value, typeof(T)) ??
            throw new InvalidOperationException("Oracle scalar conversion returned null."));
    }

    private sealed class OracleOrderOutboxFactAttribute : FactAttribute
    {
        public OracleOrderOutboxFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("SHOWTIME_RUN_ORACLE_OUTBOX_TESTS") != "1")
            {
                Skip = "SHOWTIME_RUN_ORACLE_OUTBOX_TESTS is not 1; no Oracle outbox connection will be opened.";
            }
        }
    }
}
