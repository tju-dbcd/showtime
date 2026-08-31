using System.Text.RegularExpressions;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class ExchangeMigrationTests
{
    private readonly string _script = File.ReadAllText(Path.Combine(
        FindSolutionRoot(),
        "db",
        "migrations",
        "20260830__exchange_workflow_support.sql"));

    [Fact]
    public void Migration_GuardsBothSupportedOwnerModesBeforeSchemaDdl()
    {
        var firstSchemaDdl = _script.IndexOf(
            "ALTER TABLE E_TICKET ADD CONSTRAINT CHK_ETICKET_STATUS_NEW",
            StringComparison.Ordinal);

        Assert.True(firstSchemaDdl > 0);
        var preflight = _script[..firstSchemaDdl];
        Assert.Contains("SESSION_USER", preflight, StringComparison.Ordinal);
        Assert.Contains("CURRENT_SCHEMA", preflight, StringComparison.Ordinal);
        Assert.Contains("v_session_user = 'DEPLOY_USER'", preflight, StringComparison.Ordinal);
        Assert.Contains("ALTER SESSION SET CURRENT_SCHEMA = APP_OWNER", preflight, StringComparison.Ordinal);
        Assert.Contains("v_session_user = 'LEIKAI'", preflight, StringComparison.Ordinal);
        Assert.Contains("v_current_owner = 'LEIKAI'", preflight, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "v_session_user = v_current_owner",
            preflight,
            StringComparison.Ordinal);
        Assert.Contains("RAISE_APPLICATION_ERROR", preflight, StringComparison.Ordinal);
        Assert.DoesNotContain("ALTER TABLE ", RemoveSqlStringLiterals(preflight), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE INDEX ", RemoveSqlStringLiterals(preflight), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration_UsesOwnerScopedAllDictionaryViewsAndRejectsSynonyms()
    {
        Assert.DoesNotMatch(new Regex(@"\bUSER_(TAB|CONSTRAINT|INDEX|IND)", RegexOptions.IgnoreCase), _script);
        Assert.Contains("ALL_TAB_COLUMNS", _script, StringComparison.Ordinal);
        Assert.Contains("ALL_CONSTRAINTS", _script, StringComparison.Ordinal);
        Assert.Contains("ALL_INDEXES", _script, StringComparison.Ordinal);
        Assert.Contains("ALL_IND_COLUMNS", _script, StringComparison.Ordinal);
        Assert.Contains("OWNER = v_owner", _script, StringComparison.Ordinal);
        Assert.Contains("INDEX_OWNER = v_owner", _script, StringComparison.Ordinal);
        Assert.Contains("TABLE_OWNER = v_owner", _script, StringComparison.Ordinal);
        Assert.Contains("ALL_SYNONYMS", _script, StringComparison.Ordinal);
        Assert.Contains("Synonyms are not accepted", _script, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_ExpandsTicketStatusWithValidatedTemporaryConstraint()
    {
        Assert.Contains("CHK_ETICKET_STATUS_NEW", _script, StringComparison.Ordinal);
        Assert.Contains("'EXCHANGING'", _script, StringComparison.Ordinal);
        Assert.Contains("ENABLE VALIDATE", _script, StringComparison.Ordinal);
        Assert.Contains(
            "ALTER TABLE E_TICKET DROP CONSTRAINT CHK_ETICKET_STATUS",
            _script,
            StringComparison.Ordinal);
        Assert.Contains(
            "ALTER TABLE E_TICKET RENAME CONSTRAINT CHK_ETICKET_STATUS_NEW TO CHK_ETICKET_STATUS",
            _script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_AddsPolicySnapshotAndStateConstraint()
    {
        Assert.Contains("APPLIED_POLICY_ID NUMBER(19)", _script, StringComparison.Ordinal);
        Assert.Contains("FK_EXCHANGE_APPLIED_POLICY", _script, StringComparison.Ordinal);
        Assert.Contains("REFERENCES EXCHANGE_POLICY(POLICY_ID)", _script, StringComparison.Ordinal);
        Assert.Contains("IDX_EXCHANGE_APPLIED_POLICY", _script, StringComparison.Ordinal);
        Assert.Contains("CHK_EXCHANGE_STATE_COMBO", _script, StringComparison.Ordinal);
        Assert.Contains("APPROVE_STATUS = 'REJECTED' AND EXCHANGE_STATUS = 'FAILED'", _script, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_RemovesHistoricalUniquenessButPreservesOrdinaryIndex()
    {
        Assert.Contains(
            "ALTER TABLE EXCHANGE_ITEM DROP CONSTRAINT UK_EXCHANGE_ORDER_ITEM",
            _script,
            StringComparison.Ordinal);
        Assert.Contains(
            "CREATE INDEX IDX_EXCHANGE_ITEM_ORDER ON EXCHANGE_ITEM(ORDER_ITEM_ID)",
            _script,
            StringComparison.Ordinal);
        Assert.Contains("UNIQUENESS = 'NONUNIQUE'", _script, StringComparison.Ordinal);
        Assert.Contains("COLUMN_NAME = 'ORDER_ITEM_ID'", _script, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_WidensBothByteFlagsWithoutChangingTheirDomain()
    {
        Assert.Contains("ALLOW_CROSS_SESSION NUMBER(3) DEFAULT 1", _script, StringComparison.Ordinal);
        Assert.Contains("STATUS NUMBER(3) DEFAULT 1", _script, StringComparison.Ordinal);
        Assert.Contains("AND NULLABLE = 'N'", _script, StringComparison.Ordinal);
        Assert.Contains("TRIM(v_default_value) <> '1'", _script, StringComparison.Ordinal);
        Assert.Contains("ALLOW_CROSS_SESSION NOT IN (0, 1)", _script, StringComparison.Ordinal);
        Assert.Contains("STATUS NOT IN (0, 1)", _script, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_RecognizesPartialStatesAndVerifiesTerminalState()
    {
        Assert.Contains("Forward-repair every recognized interruption boundary", _script, StringComparison.Ordinal);
        Assert.Contains("IF v_temp = 0 THEN", _script, StringComparison.Ordinal);
        Assert.Contains("IF v_count = 0 THEN", _script, StringComparison.Ordinal);
        Assert.Contains("Terminal assertions", _script, StringComparison.Ordinal);
        Assert.Contains("Terminal exchange policy precision verification failed", _script, StringComparison.Ordinal);
        Assert.DoesNotContain("ROLLBACK", _script, StringComparison.OrdinalIgnoreCase);
    }

    private static string RemoveSqlStringLiterals(string sql) =>
        Regex.Replace(sql, @"'(?:''|[^'])*'", "''", RegexOptions.Singleline);

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Showtime.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not find the Showtime solution root.");
    }
}
