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

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }
}
