using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Common;
using ShowtimeBackend.Data;
using ShowtimeBackend.Entities.OrderTicket;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class RefundModelTests
{
    [Fact]
    public void RefundModel_MapsSnapshotsAndFourConcurrencyTokens()
    {
        using var db = CreateDbContext();
        var model = db.Model;

        Assert.True(model.FindEntityType(typeof(ETicket))!
            .FindProperty(nameof(ETicket.TicketStatus))!.IsConcurrencyToken);
        Assert.True(model.FindEntityType(typeof(OrderItem))!
            .FindProperty(nameof(OrderItem.ItemStatus))!.IsConcurrencyToken);
        Assert.True(model.FindEntityType(typeof(RefundRequest))!
            .FindProperty(nameof(RefundRequest.ApproveStatus))!.IsConcurrencyToken);
        Assert.True(model.FindEntityType(typeof(RefundRequest))!
            .FindProperty(nameof(RefundRequest.RefundStatus))!.IsConcurrencyToken);
        Assert.Equal("APPLIED_POLICY_ID", model.FindEntityType(typeof(RefundRequest))!
            .FindProperty(nameof(RefundRequest.AppliedPolicyId))!.GetColumnName());
        Assert.Equal("APPLIED_SERVICE_FEE", model.FindEntityType(typeof(RefundRequest))!
            .FindProperty(nameof(RefundRequest.AppliedServiceFee))!.GetColumnName());
        Assert.Equal("REFUND_BASE_AMOUNT", model.FindEntityType(typeof(RefundItem))!
            .FindProperty(nameof(RefundItem.RefundBaseAmount))!.GetColumnName());
        Assert.Contains(ETicketStatus.REFUNDING, Enum.GetValues<ETicketStatus>());
    }

    [Fact]
    public void RefundWorkflowMigration_PerformsCompletePreflightBeforeAnySchemaChange()
    {
        var script = File.ReadAllText(Path.Combine(
            FindSolutionRoot(),
            "db",
            "migrations",
            "20260824__refund_workflow_support.sql"));
        var firstSchemaChange = script.IndexOf(
            "ALTER TABLE E_TICKET DROP CONSTRAINT CHK_ETICKET_STATUS;",
            StringComparison.Ordinal);

        Assert.True(firstSchemaChange > 0);
        var preflight = script[..firstSchemaChange];
        Assert.Contains("ALL_TAB_COLUMNS", preflight, StringComparison.Ordinal);
        Assert.Contains("'APPLIED_POLICY_ID'", preflight, StringComparison.Ordinal);
        Assert.Contains("'APPLIED_SERVICE_FEE'", preflight, StringComparison.Ordinal);
        Assert.Contains("'REFUND_BASE_AMOUNT'", preflight, StringComparison.Ordinal);
        Assert.Contains("ALL_CONSTRAINTS", preflight, StringComparison.Ordinal);
        Assert.Contains("CONSTRAINT_NAME = 'CHK_ETICKET_STATUS'", preflight, StringComparison.Ordinal);
        Assert.Contains("SEARCH_CONDITION_VC", preflight, StringComparison.Ordinal);
        Assert.Contains("'REFUNDING'", preflight, StringComparison.Ordinal);
        Assert.Contains("'FK_REFUND_APPLIED_POLICY'", preflight, StringComparison.Ordinal);
        Assert.Contains("'CHK_REFUND_APPLIED_FEE'", preflight, StringComparison.Ordinal);
        Assert.Contains("'CHK_REFUND_ACTUAL_POSITIVE'", preflight, StringComparison.Ordinal);
        Assert.Contains("'CHK_REFUND_STATE_COMBO'", preflight, StringComparison.Ordinal);
        Assert.Contains("'CHK_REFUND_BASE_AMOUNT'", preflight, StringComparison.Ordinal);
        Assert.Contains("'CHK_REFUND_POLICY_DEADLINE'", preflight, StringComparison.Ordinal);
        Assert.Contains("ALL_INDEXES", preflight, StringComparison.Ordinal);
        Assert.Contains("'IDX_REFUND_APPLIED_POLICY'", preflight, StringComparison.Ordinal);
        Assert.Contains("RAISE_APPLICATION_ERROR", preflight, StringComparison.Ordinal);
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
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not find the Showtime solution root.");
    }
}
